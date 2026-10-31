using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// The life of an attempt between the queue and a runner (ADR-0015): offered to one runner, accepted
/// or rejected, held by heartbeats, finished — and given back or failed when the runner goes quiet
/// or the job runs out of time. Finishing a job moves its run on.
/// </summary>
public sealed class JobDispatcher
{
    private readonly AutoCanvasDbContext database;
    private readonly IJobClaimer claimer;
    private readonly IJobSignal signal;
    private readonly TimeProvider time;
    private readonly TimeSpan lease;
    private readonly RunEngine engine;

    /// <summary>Creates the dispatcher for one unit of work.</summary>
    public JobDispatcher(AutoCanvasDbContext database, IJobClaimer claimer, IJobSignal signal, ConfigurationService configuration, IOptions<QueueOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.database = database;
        this.claimer = claimer;
        this.signal = signal;
        this.time = time;
        lease = options.Value.LeaseDuration;
        engine = new RunEngine(database, configuration, time);
    }

    /// <summary>
    /// Waits until attempts may be due — after something was queued, on any server — or until
    /// <paramref name="timeout"/>. Dispatchers claim after it returns either way.
    /// </summary>
    public Task<bool> WaitForWorkAsync(TimeSpan timeout, CancellationToken cancellationToken) => signal.WaitAsync(timeout, cancellationToken);

    /// <summary>
    /// Offers the oldest attempt the runner can take to it; <see langword="null"/> when there is none.
    /// The runner must accept it before the lease ends.
    /// </summary>
    public Task<Guid?> ClaimAsync(Guid runnerId, IReadOnlySet<string> labels, IReadOnlySet<string> nodeTypes, bool runsContainers, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return claimer.ClaimAsync(database, new JobClaim(runnerId, labels, nodeTypes, runsContainers, now, now + lease), cancellationToken);
    }

    /// <summary>The runner starts the attempt it was offered; <see langword="false"/> if it is no longer its own.</summary>
    public async Task<bool> AcceptAsync(Guid attemptId, Guid runnerId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return await database.JobAttempts
            .Where(a => a.Id == attemptId && a.RunnerId == runnerId && a.Status == AttemptStatus.Offered)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AttemptStatus.Running).SetProperty(a => a.StartedAt, now).SetProperty(a => a.LeaseExpiresAt, now + lease), cancellationToken)
            .ConfigureAwait(false) == 1;
    }

    /// <summary>The runner declines the attempt it was offered; it returns to the queue for others.</summary>
    public async Task<bool> RejectAsync(Guid attemptId, Guid runnerId, CancellationToken cancellationToken)
    {
        var rejected = await database.JobAttempts
            .Where(a => a.Id == attemptId && a.RunnerId == runnerId && a.Status == AttemptStatus.Offered)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AttemptStatus.Queued).SetProperty(a => a.RunnerId, (Guid?)null).SetProperty(a => a.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken)
            .ConfigureAwait(false) == 1;
        if (rejected)
        {
            await signal.NotifyAsync(database, cancellationToken).ConfigureAwait(false);
        }

        return rejected;
    }

    /// <summary>
    /// Extends the leases of the attempts the runner says it holds (a heartbeat) and returns the ones
    /// it does not hold any more — it should stop them.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> RenewAsync(Guid runnerId, IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attemptIds);
        var until = time.GetUtcNow() + lease;
        var ids = attemptIds.ToList();
        var held = database.JobAttempts.Where(a => ids.Contains(a.Id) && a.RunnerId == runnerId && (a.Status == AttemptStatus.Offered || a.Status == AttemptStatus.Running));
        await held.ExecuteUpdateAsync(set => set.SetProperty(a => a.LeaseExpiresAt, until), cancellationToken).ConfigureAwait(false);
        var still = await held.Select(a => a.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. ids.Except(still)];
    }

    /// <summary>
    /// Ends an attempt and moves its run on: a failed job is retried when its <c>retry</c> allows,
    /// otherwise it finishes and the jobs that need it are planned. With <paramref name="runnerId"/>,
    /// only that runner's attempt is ended.
    /// </summary>
    /// <returns>Whether there was such an unfinished attempt.</returns>
    public async Task<bool> FinishAsync(Guid attemptId, Guid? runnerId, AttemptOutcome outcome, string? error, CancellationToken cancellationToken)
    {
        var attempt = await database.JobAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && (runnerId == null || a.RunnerId == runnerId) && (a.Status == AttemptStatus.Offered || a.Status == AttemptStatus.Running), cancellationToken)
            .ConfigureAwait(false);
        if (attempt is null)
        {
            return false;
        }

        var now = time.GetUtcNow();
        attempt.Status = outcome switch
        {
            AttemptOutcome.Succeeded => AttemptStatus.Succeeded,
            AttemptOutcome.Failed => AttemptStatus.Failed,
            _ => AttemptStatus.Cancelled,
        };
        attempt.FinishedAt = now;
        attempt.LeaseExpiresAt = null;
        attempt.Error = error is { Length: > JobAttempt.MaxErrorLength } ? error[..JobAttempt.MaxErrorLength] : error;

        var job = await database.RunJobs.FirstAsync(j => j.Id == attempt.RunJobId, cancellationToken).ConfigureAwait(false);
        var run = await database.Runs.FirstAsync(r => r.Id == job.RunId, cancellationToken).ConfigureAwait(false);
        var jobs = await database.RunJobs.Where(j => j.RunId == run.Id).OrderBy(j => j.Position).ToListAsync(cancellationToken).ConfigureAwait(false);

        // A job cancelled with its run already has its final status; its attempt only catches up.
        if (job.Status == RunJobStatus.Running && !(outcome == AttemptOutcome.Failed && engine.Retry(run, job, attempt.Number)))
        {
            job.Status = outcome switch
            {
                AttemptOutcome.Succeeded => RunJobStatus.Succeeded,
                AttemptOutcome.Failed => RunJobStatus.Failed,
                _ => RunJobStatus.Cancelled,
            };
            job.FinishedAt = now;
        }

        await engine.AdvanceAsync(run, jobs, cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Jobs that needed this one may have been queued. A retry waits for its delay; dispatchers
        // find it on their next look after the delay, as waiting has a timeout.
        await signal.NotifyAsync(database, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Cleans up after runners: offers nobody accepted go back to the queue, attempts whose runner
    /// stopped renewing the lease or that ran past their time limit fail (and are retried if allowed).
    /// </summary>
    /// <returns>How many attempts it changed.</returns>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var changed = await database.JobAttempts
            .Where(a => a.Status == AttemptStatus.Offered && a.LeaseExpiresAt < now)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AttemptStatus.Queued).SetProperty(a => a.RunnerId, (Guid?)null).SetProperty(a => a.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken)
            .ConfigureAwait(false);
        if (changed > 0)
        {
            await signal.NotifyAsync(database, cancellationToken).ConfigureAwait(false);
        }

        var running = await database.JobAttempts.AsNoTracking()
            .Where(a => a.Status == AttemptStatus.Running)
            .Select(a => new { a.Id, a.LeaseExpiresAt, a.StartedAt, a.TimeoutMinutes })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var attempt in running)
        {
            var error = attempt.LeaseExpiresAt < now
                ? "The runner stopped responding before the job finished."
                : attempt.StartedAt + TimeSpan.FromMinutes(attempt.TimeoutMinutes) < now
                    ? $"The job ran longer than its time limit of {attempt.TimeoutMinutes} minutes."
                    : null;
            if (error is not null && await FinishAsync(attempt.Id, null, AttemptOutcome.Failed, error, cancellationToken).ConfigureAwait(false))
            {
                changed++;
            }
        }

        return changed;
    }
}
