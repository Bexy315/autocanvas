using System.Globalization;
using System.Text;
using AutoCanvas.Forge;
using AutoCanvas.Forge.GitHub;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Instance;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// Edit → pull request (S5): a repository automation is changed in its repository, not in
/// AutoCanvas, so a change made in the editor becomes a branch with one commit and a pull request
/// on the forge, from the commit of the automation's current version. Editors of the folder may
/// propose; GitHub only, through the GitHub App.
/// </summary>
public sealed class GitHubChangeProposals(
    AutoCanvasDbContext database,
    GitHubAccess github,
    InstanceSettingsService instance,
    IAuditLog audit,
    TimeProvider time)
{
    private readonly FolderAccess access = new(database);

    /// <summary>Proposes the new text of a repository automation as a pull request (Editor).</summary>
    public async Task<UseCaseResult<ProposedChangeView>> ProposeAsync(Actor actor, Guid automationId, ChangeProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var found = await FindAsync(actor, automationId, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<ProposedChangeView>(denied);
        }

        var automation = found.Value!;
        var repository = automation.RepositoryId is { } repositoryId
            ? await database.Repositories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken).ConfigureAwait(false)
            : null;
        if (repository is not { Kind: RepositoryKind.GitHub, InstallationId: { } installation } || GitHubNames.FullNameOf(repository.Url) is not { } fullName
            || repository.ResolvedBranch is not { } baseBranch || automation.SourcePath is not { } path)
        {
            return UseCase.Failure<ProposedChangeView>(UseCaseError.Conflict(repository is null
                ? "The automation is managed in AutoCanvas; save it instead."
                : $"Changes to automations from '{repository.Name}' cannot be proposed from AutoCanvas; change {automation.SourcePath} in the repository."));
        }

        var current = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == automation.Id && v.Number == automation.CurrentVersion)
            .Select(v => new { v.Yaml, v.CommitSha })
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(current.Yaml, proposal.Yaml, StringComparison.Ordinal))
        {
            return UseCase.Failure<ProposedChangeView>(UseCaseError.Invalid("yaml", "Nothing changed; there is nothing to propose."));
        }

        if (AutomationService.Load(proposal.Yaml).Error is { } invalid)
        {
            return UseCase.Failure<ProposedChangeView>(invalid);
        }

        if (current.CommitSha is not { } commit || await github.AppAsync(cancellationToken).ConfigureAwait(false) is not { } app)
        {
            return UseCase.Failure<ProposedChangeView>(UseCaseError.Conflict("The GitHub App is not set up, or the automation's commit is not known; sync the repository and try again."));
        }

        var message = string.IsNullOrWhiteSpace(proposal.Message) ? $"Change {automation.Name} in AutoCanvas" : proposal.Message.Trim();
        var title = string.IsNullOrWhiteSpace(proposal.Title) ? message.Split('\n')[0] : proposal.Title.Trim();
        var branch = $"autocanvas/{Slug(automation.Name)}-{time.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
        GitHubPullRequest opened;
        try
        {
            var token = await github.TokenAsync(installation, cancellationToken).ConfigureAwait(false);
            var sha = await github.Client.GetFileShaAsync(app.ApiAddress, token, fullName, path, commit, cancellationToken).ConfigureAwait(false);
            await github.Client.CreateBranchAsync(app.ApiAddress, token, fullName, branch, commit, cancellationToken).ConfigureAwait(false);
            await github.Client.UpdateFileAsync(app.ApiAddress, token, fullName, path, branch, sha, proposal.Yaml, message, cancellationToken).ConfigureAwait(false);
            var body = await BodyAsync(actor, automation.Id, cancellationToken).ConfigureAwait(false);
            opened = await github.Client.CreatePullRequestAsync(app.ApiAddress, token, fullName, branch, baseBranch, title, body, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            return UseCase.Failure<ProposedChangeView>(UseCaseError.Conflict($"GitHub did not take the change: {exception.Message}"));
        }

        audit.Record(Entry(automation, ("repository", repository.Name), ("path", path), ("branch", branch), ("number", opened.Number)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new ProposedChangeView(opened.Number, opened.HtmlUrl, branch));
    }

    /// <summary>The automation, for an Editor of its folder.</summary>
    private async Task<UseCaseResult<Persistence.Automations.Automation>> FindAsync(Actor actor, Guid automationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var automation = await database.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == automationId, cancellationToken).ConfigureAwait(false);
        var notFound = UseCaseError.NotFound("The automation does not exist, or you may not see it.");
        if (automation is null)
        {
            return UseCase.Failure<Persistence.Automations.Automation>(notFound);
        }

        var folder = await access.RequireAsync(actor, automation.FolderId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
        return folder.Error is { } denied
            ? UseCase.Failure<Persistence.Automations.Automation>(denied.Kind == UseCaseErrorKind.NotFound ? notFound : denied)
            : UseCase.Success(automation);
    }

    /// <summary>The pull request's text: who proposed it, and where in AutoCanvas.</summary>
    private async Task<string> BodyAsync(Actor actor, Guid automationId, CancellationToken cancellationToken)
    {
        var who = await database.Users.AsNoTracking().Where(u => u.Id == actor.UserId).Select(u => u.DisplayName ?? u.UserName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? "someone";
        var publicAddress = (await instance.GetAsync(cancellationToken).ConfigureAwait(false)).PublicAddress.Value;
        return publicAddress is null
            ? $"Proposed by {who} in AutoCanvas."
            : $"Proposed by {who} in AutoCanvas: {new Uri(publicAddress, $"/automations/{automationId}")}";
    }

    private static AuditEntry Entry(Persistence.Automations.Automation automation, params (string Key, object? Value)[] details) => new(AuditActions.AutomationPullRequestProposed)
    {
        TargetType = "automation",
        TargetId = automation.Id.ToString(),
        FolderId = automation.FolderId,
        Details = AuditEntry.DetailsOf(details),
    };

    /// <summary>A name as part of a branch: letters, digits, dots, dashes and underscores.</summary>
    private static string Slug(string name)
    {
        var slug = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            slug.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        }

        return slug.ToString().Trim('.', '-');
    }
}
