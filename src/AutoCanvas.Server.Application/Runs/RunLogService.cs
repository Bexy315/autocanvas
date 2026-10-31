using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Reports;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Reading what happened inside a run's jobs: their attempts, steps and outputs, and their logs.
/// Needs Viewer on the run's folder (ADR-0012), like the run itself.
/// </summary>
public sealed class RunLogService(AutoCanvasDbContext database)
{
    /// <summary>Chunks a page has when the caller does not say.</summary>
    public const int DefaultChunks = 100;

    /// <summary>The most chunks one page can have.</summary>
    public const int MaxChunks = 1000;

    private const int MaxMissing = 100;

    private readonly RunAccess access = new(database);

    /// <summary>A job of the run with its outputs and attempts.</summary>
    public async Task<UseCaseResult<RunJobDetailView>> GetJobAsync(Actor actor, Guid runId, string jobKey, CancellationToken cancellationToken)
    {
        if (await access.ViewAsync(actor, runId, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return UseCase.Failure<RunJobDetailView>(denied);
        }

        var job = await database.RunJobs.AsNoTracking().FirstOrDefaultAsync(j => j.RunId == runId && j.Key == jobKey, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return UseCase.Failure<RunJobDetailView>(UseCaseError.NotFound("The run has no such job."));
        }

        var attempts = await database.JobAttempts.AsNoTracking().Where(a => a.RunJobId == job.Id).OrderBy(a => a.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = attempts.Select(a => a.Id).ToList();
        var steps = await database.JobSteps.AsNoTracking().Where(s => ids.Contains(s.AttemptId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var runnerIds = attempts.Select(a => a.RunnerId).OfType<Guid>().Distinct().ToList();
        var reports = await database.Reports.AsNoTracking().Where(r => ids.Contains(r.AttemptId)).OrderBy(r => r.ReceivedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        var runners = await database.Runners.AsNoTracking().Where(r => runnerIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new RunJobDetailView(
            job.Key,
            job.Name,
            job.Status,
            job.Reason,
            RunOutputs.Read(job.Outputs),
            job.StartedAt,
            job.FinishedAt,
            [.. attempts.Select(a => new JobAttemptView(
                a.Id,
                a.Number,
                a.Status,
                a.RunnerId,
                a.RunnerId is { } runner ? runners.GetValueOrDefault(runner) : null,
                a.QueuedAt,
                a.StartedAt,
                a.FinishedAt,
                a.Error,
                a.Warnings is null ? [] : JsonSerializer.Deserialize<string[]>(a.Warnings) ?? [],
                [.. steps.Where(s => s.AttemptId == a.Id).OrderBy(s => s.Index).Select(s => new JobStepView(
                    s.Index, s.StepId, s.Name, s.Status, s.Reason, s.ExitCode, RunOutputs.Read(s.Outputs), s.Error, s.StartedAt, s.FinishedAt))],
                [.. reports.Where(r => r.AttemptId == a.Id).Select(ReportSummaryView.Of)]))]));
    }

    /// <summary>The chunks of an attempt's log after <paramref name="after"/> (from the start when <see langword="null"/>).</summary>
    public async Task<UseCaseResult<LogPage>> ReadAsync(Actor actor, Guid runId, Guid attemptId, long? after, int? limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaxChunks)
        {
            return UseCase.Failure<LogPage>(UseCaseError.Invalid("limit", $"limit must be between 1 and {MaxChunks}."));
        }

        if (await access.ViewAsync(actor, runId, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return UseCase.Failure<LogPage>(denied);
        }

        var attempt = await database.JobAttempts.AsNoTracking()
            .Where(a => a.Id == attemptId && database.RunJobs.Any(j => j.Id == a.RunJobId && j.RunId == runId))
            .Select(a => new { a.Status, a.LogTruncated })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (attempt is null)
        {
            return UseCase.Failure<LogPage>(UseCaseError.NotFound("The run has no such job attempt."));
        }

        var from = after ?? -1;
        var take = limit ?? DefaultChunks;
        var chunks = await database.LogChunks.AsNoTracking()
            .Where(c => c.AttemptId == attemptId && c.Sequence > from)
            .OrderBy(c => c.Sequence)
            .Take(take + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var more = chunks.Count > take;
        var page = chunks.Take(take).ToList();
        var missing = new List<long>();
        var expected = from + 1;
        foreach (var chunk in page)
        {
            for (var gap = expected; gap < chunk.Sequence && missing.Count < MaxMissing; gap++)
            {
                missing.Add(gap);
            }

            expected = chunk.Sequence + 1;
        }

        var finished = attempt.Status is AttemptStatus.Succeeded or AttemptStatus.Failed or AttemptStatus.Cancelled;
        return UseCase.Success(new LogPage(
            attemptId,
            [.. page.Select(c => new LogChunkView(c.Sequence, c.StepIndex, JsonSerializer.Deserialize<LogLine[]>(c.Lines, LogIngestion.Json) ?? []))],
            page.Count > 0 ? page[^1].Sequence : from,
            missing,
            finished && !more,
            attempt.LogTruncated));
    }
}
