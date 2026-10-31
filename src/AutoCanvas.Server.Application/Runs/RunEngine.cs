using AutoCanvas.Core;
using AutoCanvas.Core.Model;
using AutoCanvas.Core.Orchestration;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using CoreRunState = AutoCanvas.Core.Orchestration.RunState;
using StoredRunState = AutoCanvas.Persistence.Runs.RunState;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Moves a run forward: asks Core's <see cref="RunPlanner"/> what happens next from the stored
/// state, applies its decisions to the jobs, queues an attempt for every job that starts, and
/// closes the run once every job is finished. It changes entities; the caller saves them.
/// </summary>
internal sealed class RunEngine(AutoCanvasDbContext database, ConfigurationService configuration, TimeProvider time)
{
    /// <summary>Plans and applies everything that can happen now.</summary>
    public async Task AdvanceAsync(Run run, IReadOnlyList<RunJob> jobs, CancellationToken cancellationToken)
    {
        var automation = Load(run);
        var values = await ValuesAsync(run, automation, jobs, cancellationToken).ConfigureAwait(false);
        Apply(automation, jobs, RunPlanner.Plan(automation, State(run, jobs), values));
        Close(run, automation, jobs);
    }

    /// <summary>Stops every job that is not finished, then lets finally jobs that run on cancel start.</summary>
    public async Task CancelAsync(Run run, IReadOnlyList<RunJob> jobs, CancellationToken cancellationToken)
    {
        var automation = Load(run);

        // Loaded so that cancelling a job finds its unfinished attempt among the tracked entities.
        var jobIds = jobs.Select(j => j.Id).ToList();
        await database.JobAttempts
            .Where(a => jobIds.Contains(a.RunJobId) && (a.Status == AttemptStatus.Queued || a.Status == AttemptStatus.Offered || a.Status == AttemptStatus.Running))
            .LoadAsync(cancellationToken).ConfigureAwait(false);
        Apply(automation, jobs, RunPlanner.Cancel(automation, State(run, jobs)));
        await AdvanceAsync(run, jobs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The jobs a new run starts with, all pending.</summary>
    public IReadOnlyList<RunJob> CreateJobs(Run run, AutomationDefinition automation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(automation);
        var jobs = automation.Jobs.Select((job, position) => new RunJob
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            RunId = run.Id,
            Position = position,
            Key = job.Id,
            Name = Cut(job.Name ?? job.Id, RunJob.MaxNameLength)!,
        }).ToList();
        database.RunJobs.AddRange(jobs);
        return jobs;
    }

    /// <summary>The run's automation, parsed once per run and server (<see cref="RunDefinitions"/>).</summary>
    internal static AutomationDefinition Load(Run run) => RunDefinitions.Get(run);

    private static CoreRunState State(Run run, IReadOnlyList<RunJob> jobs) =>
        new(jobs.ToDictionary(j => j.Key, j => new JobState(ToCore(j.Status), j.ActivatedCase, Math.Max(1, j.AttemptCount)), StringComparer.Ordinal), run.CancelRequested);

    internal async Task<RunValues> ValuesAsync(Run run, AutomationDefinition automation, IReadOnlyList<RunJob> jobs, CancellationToken cancellationToken)
    {
        var resolved = await configuration.ResolveForRunAsync(run.FolderId, run.AutomationId, automation.Variables, cancellationToken).ConfigureAwait(false);
        var status = RunOutcome.Describe(RunOutcome.OfMainJobs(automation, State(run, jobs)));
        var outputs = jobs.Where(j => j.Outputs is not null).ToDictionary(j => j.Key, j => RunOutputs.Read(j.Outputs), StringComparer.Ordinal);
        return new RunValues(run, resolved, status, outputs);
    }

    private void Apply(AutomationDefinition automation, IReadOnlyList<RunJob> jobs, IReadOnlyList<JobAction> actions)
    {
        var now = time.GetUtcNow();
        foreach (var action in actions)
        {
            var job = jobs.First(j => string.Equals(j.Key, action.JobId, StringComparison.Ordinal));
            switch (action)
            {
                case StartJob:
                    job.Status = RunJobStatus.Running;
                    job.StartedAt = now;
                    Enqueue(automation, job, 1, now);
                    break;
                case SkipJob skip:
                    Finish(job, RunJobStatus.Skipped, now, skip.Reason);
                    break;
                case CompleteSwitch complete:
                    job.ActivatedCase = Cut(complete.ActivatedCase, RunJob.MaxNameLength);
                    Finish(job, RunJobStatus.Succeeded, now, complete.ActivatedCase is null ? $"No case matched '{complete.Value}'." : $"'{complete.Value}' chose {complete.ActivatedCase}.");
                    break;
                case CompleteJoin:
                    Finish(job, RunJobStatus.Succeeded, now, null);
                    break;
                case CancelJob:
                    Finish(job, RunJobStatus.Cancelled, now, null);
                    CancelQueuedAttempts(job, now);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown action {action.GetType().Name} for job '{action.JobId}' of '{automation.Name}'.");
            }
        }
    }

    /// <summary>
    /// Queues the next attempt of a failed job if its <c>retry</c> allows one, after the retry delay.
    /// </summary>
    /// <returns>Whether an attempt was queued.</returns>
    public bool Retry(Run run, RunJob job, int failedAttempt)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(job);
        var automation = Load(run);
        if (run.CancelRequested
            || automation.Jobs.FirstOrDefault(j => string.Equals(j.Id, job.Key, StringComparison.Ordinal)) is not StepJob step
            || RunPlanner.RetryDelay(step, failedAttempt) is not { } delay)
        {
            return false;
        }

        Enqueue(automation, job, failedAttempt + 1, time.GetUtcNow() + delay);
        return true;
    }

    /// <summary>The job's time limit: its own, else the automation's, else <see cref="DefaultTimeoutMinutes"/>.</summary>
    public const int DefaultTimeoutMinutes = 60;

    /// <summary>
    /// Queues an attempt with what a runner needs to take it: all <c>runs_on</c> labels, every node
    /// type of its steps, a container executor for an image or services, and the time limit.
    /// </summary>
    private void Enqueue(AutomationDefinition automation, RunJob job, int number, DateTimeOffset notBefore)
    {
        var step = (StepJob)automation.Jobs.First(j => string.Equals(j.Id, job.Key, StringComparison.Ordinal));
        var now = time.GetUtcNow();
        job.AttemptCount = number;
        database.JobAttempts.Add(new JobAttempt
        {
            Id = Guid.CreateVersion7(now),
            RunJobId = job.Id,
            Number = number,
            QueuedAt = now,
            NotBefore = notBefore,
            Labels = JobRequirements.Write(step.RunsOn),
            NodeTypes = JobRequirements.Write(step.Steps.OfType<NodeStep>().Select(n => n.Type)),
            NeedsContainer = step.Image is not null || step.Services.Count > 0,
            TimeoutMinutes = step.TimeoutMinutes ?? automation.TimeoutMinutes ?? DefaultTimeoutMinutes,
        });
    }

    /// <summary>
    /// Attempts no runner has started are cancelled here — a runner that accepts an offer now is told
    /// it was withdrawn. A running attempt is stopped through its runner: it gets a reason, which its
    /// runner's session sends as <c>CancelJob</c>, and catches up when the runner reports it finished.
    /// </summary>
    private void CancelQueuedAttempts(RunJob job, DateTimeOffset now)
    {
        foreach (var attempt in database.JobAttempts.Local.Where(a => a.RunJobId == job.Id).ToList())
        {
            if (attempt.Status is AttemptStatus.Queued or AttemptStatus.Offered)
            {
                attempt.Status = AttemptStatus.Cancelled;
                attempt.FinishedAt = now;
                attempt.LeaseExpiresAt = null;
            }
            else if (attempt.Status == AttemptStatus.Running)
            {
                attempt.CancelReason ??= "The run was cancelled.";
            }
        }
    }

    private void Close(Run run, AutomationDefinition automation, IReadOnlyList<RunJob> jobs)
    {
        if (jobs.Any(j => j.Status is RunJobStatus.Pending or RunJobStatus.Running))
        {
            return;
        }

        run.State = RunOutcome.Of(automation, State(run, jobs)) switch
        {
            RunStatus.Succeeded => StoredRunState.Succeeded,
            RunStatus.Cancelled => StoredRunState.Cancelled,
            RunStatus.Failed => StoredRunState.Failed,
            _ => StoredRunState.Running,
        };
        run.FinishedAt = run.State == StoredRunState.Running ? null : time.GetUtcNow();
    }

    private static void Finish(RunJob job, RunJobStatus status, DateTimeOffset now, string? reason)
    {
        job.Status = status;
        job.Reason = Cut(reason, RunJob.MaxReasonLength);
        job.FinishedAt = now;
    }

    private static JobStatus ToCore(RunJobStatus status) => status switch
    {
        RunJobStatus.Pending => JobStatus.Pending,
        RunJobStatus.Running => JobStatus.Running,
        RunJobStatus.Succeeded => JobStatus.Succeeded,
        RunJobStatus.Failed => JobStatus.Failed,
        RunJobStatus.Cancelled => JobStatus.Cancelled,
        _ => JobStatus.Skipped,
    };

    private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;
}
