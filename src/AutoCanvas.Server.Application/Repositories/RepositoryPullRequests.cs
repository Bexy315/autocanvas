using System.Globalization;
using System.Text.Json;
using AutoCanvas.Forge;
using AutoCanvas.Forge.GitHub;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// Handles a pull request event of a connected repository: every repository automation whose file
/// exists at the pull request's head and whose <c>triggers.pull_request</c> there matches starts a
/// run with that file, at that commit. A pull request from a fork runs without secrets.
/// </summary>
internal sealed class RepositoryPullRequests
{
    private readonly AutoCanvasDbContext database;
    private readonly RepositoryDiscovery discovery;
    private readonly GitHubAccess github;
    private readonly RunStarter starter;

    public RepositoryPullRequests(AutoCanvasDbContext database, RepositoryDiscovery discovery, GitHubAccess github, IJobSignal signal, ConfigurationService configuration, IAuditLog audit, TimeProvider time)
    {
        this.database = database;
        this.discovery = discovery;
        this.github = github;
        starter = new RunStarter(database, signal, audit, new RunEngine(database, configuration, time), time);
    }

    /// <summary>The ref forges keep a pull request's head at, in the repository it goes into.</summary>
    public static string HeadRef(int number) => $"refs/pull/{number.ToString(CultureInfo.InvariantCulture)}/head";

    public async Task HandleAsync(Repository repository, RepositoryPullRequest pull, TriggerRound round, CancellationToken cancellationToken)
    {
        var gitRef = HeadRef(pull.Number);
        RepositorySnapshot snapshot;
        try
        {
            var access = await discovery.AccessAsync(repository, cancellationToken).ConfigureAwait(false);
            snapshot = await discovery.ForgeOf(repository).ReadAutomationsAsync(access, gitRef, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            round.Skip($"Pull request #{pull.Number} of the repository '{repository.Name}' started nothing: {exception.Message}");
            return;
        }

        if (!string.Equals(snapshot.Commit, pull.HeadSha, StringComparison.Ordinal))
        {
            // New commits came since; their own event starts the runs.
            return;
        }

        IReadOnlyList<string>? changed = null;
        var askedForChanges = false;
        var automations = await database.Automations.AsNoTracking().Where(a => a.RepositoryId == repository.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var file in snapshot.Files)
        {
            if (automations.FirstOrDefault(a => string.Equals(a.SourcePath, file.Path, StringComparison.Ordinal)) is not { } automation
                || AutomationService.Load(file.Content).Value is not { Triggers.PullRequest: { } trigger } definition)
            {
                continue;
            }

            if (trigger.Paths.Count > 0 && !askedForChanges)
            {
                askedForChanges = true;
                changed = await ChangedFilesAsync(repository, pull.Number, cancellationToken).ConfigureAwait(false);
            }

            if (trigger.Matches(pull.Action, pull.BaseBranch, changed))
            {
                await StartAsync(automation, file.Content, definition, pull, gitRef, round, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task StartAsync(Automation automation, string yaml, Core.Model.AutomationDefinition definition, RepositoryPullRequest pull, string gitRef, TriggerRound round, CancellationToken cancellationToken)
    {
        var start = new RunStart(RunTriggerKind.PullRequest, null, pull.Author, Data(pull, gitRef))
        {
            Commit = pull.HeadSha,
            Ref = gitRef,
            PullRequest = pull.Number,
            SecretsWithheld = pull.Fork,
        };
        var started = await starter.StartAsync(automation, yaml, definition, start, cancellationToken).ConfigureAwait(false);
        if (started.Error is { } refused)
        {
            round.Skip($"'{automation.Name}' did not start (pull request #{pull.Number}): {refused.Message}");
            return;
        }

        round.Start($"'{automation.Name}' #{started.Value!.Number}");
    }

    /// <summary>The files the pull request changes, asked from GitHub; <see langword="null"/> when not known.</summary>
    private async Task<IReadOnlyList<string>?> ChangedFilesAsync(Repository repository, int number, CancellationToken cancellationToken)
    {
        if (repository.Kind != RepositoryKind.GitHub || repository.InstallationId is not { } installation
            || GitHubNames.FullNameOf(repository.Url) is not { } fullName
            || await github.AppAsync(cancellationToken).ConfigureAwait(false) is not { } app)
        {
            return null;
        }

        try
        {
            var token = await github.TokenAsync(installation, cancellationToken).ConfigureAwait(false);
            return await github.Client.ListPullRequestFilesAsync(app.ApiAddress, token, fullName, number, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException)
        {
            // Not known; any change counts then.
            return null;
        }
    }

    /// <summary>What the run reads as <c>trigger.*</c>.</summary>
    private static string Data(RepositoryPullRequest pull, string gitRef) => JsonSerializer.Serialize(new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["number"] = pull.Number.ToString(CultureInfo.InvariantCulture),
        ["title"] = pull.Title,
        ["action"] = pull.Action,
        ["head_branch"] = pull.HeadBranch,
        ["base_branch"] = pull.BaseBranch,
        ["ref"] = gitRef,
        ["sha"] = pull.HeadSha,
        ["short_sha"] = pull.HeadSha[..7],
        ["author"] = pull.Author,
        ["fork"] = pull.Fork ? "true" : "false",
        ["url"] = pull.Url?.ToString(),
    });
}
