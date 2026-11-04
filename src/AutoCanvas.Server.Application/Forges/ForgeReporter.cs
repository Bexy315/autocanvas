using AutoCanvas.Forge;
using AutoCanvas.Forge.GitHub;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Forges;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Instance;
using AutoCanvas.Server.Application.Reports;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Triggers;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// Reports runs of repository automations back to their forge (S5): a commit status when a run
/// starts and when it ends, and, for runs of a pull request, the comment on it with each
/// automation's latest result for the head commit. Each change of a run's state is taken once,
/// with a conditional update, so that of several servers one reports it. A report that fails is
/// logged and not tried again. Forges without statuses (Generic Git) are passed over.
/// </summary>
public sealed class ForgeReporter(
    AutoCanvasDbContext database,
    GitHubAccess github,
    RunReportService reports,
    InstanceSettingsService instance,
    TimeProvider time)
{
    /// <summary>Runs reported per round; the rest waits for the next.</summary>
    public const int BatchSize = 50;

    /// <summary>Reports every run whose state changed since it was last reported.</summary>
    public async Task<TriggerRound> ReportAsync(CancellationToken cancellationToken)
    {
        var changed = await database.Runs.AsNoTracking()
            .Where(r => r.RepositoryId != null && (r.ReportedState == null || r.ReportedState != r.State))
            .OrderBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var round = new TriggerRound();
        if (changed.Count == 0)
        {
            return round;
        }

        var publicAddress = (await instance.GetAsync(cancellationToken).ConfigureAwait(false)).PublicAddress.Value;
        foreach (var run in changed)
        {
            var taken = await database.Runs
                .Where(r => r.Id == run.Id && r.ReportedState == run.ReportedState)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.ReportedState, run.State), cancellationToken).ConfigureAwait(false);
            if (taken == 0)
            {
                continue;
            }

            try
            {
                await ReportAsync(run, publicAddress, round, cancellationToken).ConfigureAwait(false);
            }
            catch (ForgeException exception)
            {
                round.Skip($"Run #{run.Number} of '{run.AutomationName}' was not reported to GitHub: {exception.Message}");
            }
        }

        return round;
    }

    private async Task ReportAsync(Run run, Uri? publicAddress, TriggerRound round, CancellationToken cancellationToken)
    {
        var repository = await database.Repositories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == run.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repository is not { Kind: RepositoryKind.GitHub, InstallationId: { } installation } || run.CommitSha is not { } commit
            || GitHubNames.FullNameOf(repository.Url) is not { } fullName
            || await github.AppAsync(cancellationToken).ConfigureAwait(false) is not { } app)
        {
            // Generic Git has no statuses; a run without a commit has nothing to report on.
            return;
        }

        var token = await github.TokenAsync(installation, cancellationToken).ConfigureAwait(false);
        var link = publicAddress is null ? null : new Uri(publicAddress, $"/runs/{run.Id}");
        var totals = run.State == RunState.Running ? null : await reports.ReportsAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var took = run.FinishedAt is { } finished ? finished - run.CreatedAt : (TimeSpan?)null;
        var status = new GitHubStatus(
            State(run.State),
            $"AutoCanvas / {run.AutomationName} ({RunValues.TriggerEvent(run.Trigger)})",
            PullRequestCommentText.Description(run.State, totals?.Tests, took),
            link);
        await github.Client.CreateStatusAsync(app.ApiAddress, token, fullName, commit, status, cancellationToken).ConfigureAwait(false);
        round.Start($"'{run.AutomationName}' #{run.Number}: {status.State}");

        if (run.PullRequest is { } number && run.State != RunState.Running)
        {
            await CommentAsync(app, token, fullName, repository, number, commit, publicAddress, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes the pull request's comment with the latest run of each automation for the commit.</summary>
    private async Task CommentAsync(GitHubApp app, string token, string fullName, Repository repository, int number, string commit, Uri? publicAddress, CancellationToken cancellationToken)
    {
        var runs = await database.Runs.AsNoTracking()
            .Where(r => r.RepositoryId == repository.Id && r.PullRequest == number && r.CommitSha == commit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<PullRequestCommentText.Row>();
        foreach (var latest in runs.GroupBy(r => r.AutomationName, StringComparer.Ordinal).Select(g => g.MaxBy(r => r.Number)!))
        {
            var link = publicAddress is null ? null : new Uri(publicAddress, $"/runs/{latest.Id}");
            rows.Add(new PullRequestCommentText.Row(latest.AutomationName, latest.Number, latest.State, link, await reports.ReportsAsync(latest.Id, cancellationToken).ConfigureAwait(false)));
        }

        var body = PullRequestCommentText.Of(commit, rows);
        var stored = await database.PullRequestComments.FirstOrDefaultAsync(c => c.RepositoryId == repository.Id && c.Number == number, cancellationToken).ConfigureAwait(false);
        if (stored is not null && await github.Client.UpdateIssueCommentAsync(app.ApiAddress, token, fullName, stored.CommentId, body, cancellationToken).ConfigureAwait(false))
        {
            stored.UpdatedAt = time.GetUtcNow();
        }
        else
        {
            // No comment yet, or someone deleted it: a new one.
            var id = await github.Client.CreateIssueCommentAsync(app.ApiAddress, token, fullName, number, body, cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                database.PullRequestComments.Add(new PullRequestComment { RepositoryId = repository.Id, Number = number, CommentId = id, UpdatedAt = time.GetUtcNow() });
            }
            else
            {
                (stored.CommentId, stored.UpdatedAt) = (id, time.GetUtcNow());
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string State(RunState state) => state switch
    {
        RunState.Running => "pending",
        RunState.Succeeded => "success",
        RunState.Failed => "failure",
        _ => "error",
    };
}
