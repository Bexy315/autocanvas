using AutoCanvas.Forge;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Repositories;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// GitHub repositories in folders (ADR-0022): editors of a folder choose one from the repositories
/// the GitHub App's installations reach, and it is connected like any repository, reached with
/// installation tokens instead of a credential of its own.
/// </summary>
public sealed class GitHubRepositoryService(AutoCanvasDbContext database, GitHubAccess github, RepositoryService repositories, TimeProvider time)
{
    private readonly FolderAccess access = new(database);

    /// <summary>The repositories every installation of the app reaches, by name (Editor of the folder they would be connected in).</summary>
    public async Task<UseCaseResult<IReadOnlyList<GitHubRepositoryView>>> ListAsync(Actor actor, Guid folderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var folder = await access.RequireAsync(actor, folderId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<IReadOnlyList<GitHubRepositoryView>>(denied);
        }

        if (await github.AppAsync(cancellationToken).ConfigureAwait(false) is not { } app)
        {
            return UseCase.Failure<IReadOnlyList<GitHubRepositoryView>>(NoApp);
        }

        try
        {
            var found = new List<GitHubRepositoryView>();
            foreach (var installation in await github.Client.ListInstallationsAsync(app.ApiAddress, github.JwtOf(app), cancellationToken).ConfigureAwait(false))
            {
                if (installation.Suspended)
                {
                    continue;
                }

                var token = await github.TokenAsync(installation.Id, cancellationToken).ConfigureAwait(false);
                var reached = await github.Client.ListInstallationRepositoriesAsync(app.ApiAddress, token, cancellationToken).ConfigureAwait(false);
                found.AddRange(reached.Select(r => new GitHubRepositoryView(installation.Id, installation.Account, r.Id, r.FullName, r.DefaultBranch, r.Private)));
            }

            return UseCase.Success<IReadOnlyList<GitHubRepositoryView>>([.. found.OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)]);
        }
        catch (ForgeException exception)
        {
            return UseCase.Failure<IReadOnlyList<GitHubRepositoryView>>(UseCaseError.Conflict($"GitHub cannot be asked for the repositories now: {exception.Message}"));
        }
    }

    /// <summary>Connects a repository of an installation of the app in a folder and reads its automations (Editor).</summary>
    public async Task<UseCaseResult<RepositoryView>> ConnectAsync(Actor actor, Guid folderId, NewGitHubRepository request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        var folder = await access.RequireAsync(actor, folderId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<RepositoryView>(denied);
        }

        if (await github.AppAsync(cancellationToken).ConfigureAwait(false) is not { } app)
        {
            return UseCase.Failure<RepositoryView>(NoApp);
        }

        Forge.GitHub.GitHubRepository found;
        try
        {
            var token = await github.TokenAsync(request.InstallationId, cancellationToken).ConfigureAwait(false);
            found = await github.Client.GetRepositoryAsync(app.ApiAddress, token, request.Repository?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            return UseCase.Failure<RepositoryView>(UseCaseError.Invalid("repository", $"The app's installation does not reach that repository: {exception.Message}"));
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? found.FullName.Split('/')[^1] : request.Name.Trim();
        if (RepositoryService.Check(name, request.DefaultBranch, request.PollMinutes) is { } invalid)
        {
            return UseCase.Failure<RepositoryView>(invalid);
        }

        var now = time.GetUtcNow();
        return await repositories.AddAsync(actor, new Repository
        {
            Id = Guid.CreateVersion7(now),
            FolderId = folderId,
            Name = name,
            NormalizedName = IdentityNames.Normalize(name),
            Kind = RepositoryKind.GitHub,
            Url = found.CloneUrl,
            DefaultBranch = RepositoryService.Empty(request.DefaultBranch),
            InstallationId = request.InstallationId,
            ExternalId = found.Id,
            PollMinutes = request.PollMinutes ?? Repository.DefaultPollMinutes,
            CreatedAt = now,
            CreatedByName = await repositories.UserNameAsync(actor, cancellationToken).ConfigureAwait(false),
            UpdatedAt = now,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static UseCaseError NoApp => UseCaseError.Conflict("No GitHub App is set up; an admin sets it up under Administration, Forges.");
}
