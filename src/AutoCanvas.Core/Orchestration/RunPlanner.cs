using AutoCanvas.Core.Expressions;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Orchestration;

/// <summary>
/// Decides what happens next in a run: which pending jobs start, which are skipped, and how
/// switches and joins resolve. Pure: it reads a snapshot of the run and returns actions; the
/// server carries them out and asks again when a job changes.
/// </summary>
public static class RunPlanner
{
    /// <summary>
    /// The actions that follow from <paramref name="state"/>. Decisions build on each other within
    /// one call — a skipped job can make its dependents skippable, a resolved switch can start the
    /// jobs of its case — so the result is complete until a running job finishes.
    /// </summary>
    /// <param name="automation">The automation the run was started with.</param>
    /// <param name="state">The run as it stands.</param>
    /// <param name="values">Supplies values for <c>if</c> conditions and switch expressions. Paths
    /// <c>jobs.JOB.result</c> are answered from the run state instead, so they reflect decisions
    /// made within this call.</param>
    /// <returns>The actions, in the order they were decided.</returns>
    public static IReadOnlyList<JobAction> Plan(AutomationDefinition automation, RunState state, IValueResolver values)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(values);

        var jobs = automation.Jobs.ToDictionary(j => j.Id, j => state.Of(j.Id), StringComparer.Ordinal);
        var resolver = new RunStateResolver(jobs, values);
        var actions = new List<JobAction>();
        bool changed;
        do
        {
            changed = false;
            foreach (var job in automation.Jobs.Where(j => jobs[j.Id].Status == JobStatus.Pending))
            {
                if (Decide(automation, job, jobs, state.CancelRequested, resolver) is { } action)
                {
                    actions.Add(action);
                    jobs[job.Id] = Apply(action, jobs[job.Id]);
                    changed = true;
                }
            }
        }
        while (changed);

        return actions;
    }

    /// <summary>
    /// What cancelling a run means for its jobs: every pending or running job is cancelled, except
    /// finally jobs with <c>on_cancel</c> that have not started — they still run, once the others
    /// have stopped.
    /// </summary>
    /// <param name="automation">The automation the run was started with.</param>
    /// <param name="state">The run as it stands.</param>
    /// <returns>A <see cref="CancelJob"/> for each job to stop.</returns>
    public static IReadOnlyList<JobAction> Cancel(AutomationDefinition automation, RunState state)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(state);

        return automation.Jobs
            .Where(job => !state.Of(job.Id).IsFinished)
            .Where(job => !(job is StepJob { RunsFinally: true, RunsOnCancel: true } && state.Of(job.Id).Status == JobStatus.Pending))
            .Select(job => (JobAction)new CancelJob(job.Id))
            .ToList();
    }

    /// <summary>Whether a failed job gets another attempt, and after which delay.</summary>
    /// <param name="job">The job that failed.</param>
    /// <param name="failedAttempt">The attempt that failed, starting at 1.</param>
    /// <returns>The delay before the next attempt, or <see langword="null"/> when no attempt is left.</returns>
    public static TimeSpan? RetryDelay(StepJob job, int failedAttempt)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Retry is { } retry && failedAttempt < retry.MaxAttempts ? retry.Delay : null;
    }

    private static JobAction? Decide(AutomationDefinition automation, JobDefinition job, Dictionary<string, JobState> jobs, bool cancelRequested, RunStateResolver resolver)
    {
        if (job is StepJob { RunsFinally: true } finallyJob)
        {
            return DecideFinally(automation, finallyJob, jobs, cancelRequested);
        }

        if (cancelRequested)
        {
            // Normally Cancel already stopped these; this covers a job that became pending later.
            return new CancelJob(job.Id);
        }

        if (job.Needs.Any(need => !jobs.GetValueOrDefault(need.Job, JobState.Pending).IsFinished))
        {
            return null;
        }

        return UnmetNeed(job, jobs) is { } reason
            ? new SkipJob(job.Id, reason)
            : Ready(job, resolver);
    }

    private static JobAction? DecideFinally(AutomationDefinition automation, StepJob job, Dictionary<string, JobState> jobs, bool cancelRequested)
    {
        var othersDone = automation.Jobs
            .Where(other => other is not StepJob { RunsFinally: true })
            .All(other => jobs[other.Id].IsFinished);
        if (!othersDone)
        {
            return null;
        }

        if (cancelRequested && !job.RunsOnCancel)
        {
            return new SkipJob(job.Id, "The run was cancelled; this finally job does not run on cancel.");
        }

        return new StartJob(job.Id);
    }

    /// <summary>Why the job's needs rule it out, or <see langword="null"/> if they are met.</summary>
    private static string? UnmetNeed(JobDefinition job, Dictionary<string, JobState> jobs)
    {
        foreach (var need in job.Needs)
        {
            var other = jobs.GetValueOrDefault(need.Job, JobState.Pending);
            if (need.Case is { } caseName)
            {
                if (other.Status != JobStatus.Succeeded || !string.Equals(other.ActivatedCase, caseName, StringComparison.Ordinal))
                {
                    var activated = other.Status == JobStatus.Succeeded ? $"activated {Describe(other.ActivatedCase)}" : Describe(other.Status);
                    return $"'{need.Job}' {activated}, but this job runs on case '{caseName}'.";
                }

                continue;
            }

            var met = need.On switch
            {
                OutcomeFilter.Failure => other.Status == JobStatus.Failed,
                OutcomeFilter.Always => true,
                _ => other.Status == JobStatus.Succeeded,
            };
            if (!met)
            {
                var expected = need.On == OutcomeFilter.Failure ? "failed" : "succeeded";
                return $"'{need.Job}' {Describe(other.Status)}, but this job needs it to have {expected}.";
            }
        }

        return null;
    }

    private static JobAction Ready(JobDefinition job, RunStateResolver resolver)
    {
        if (job.Condition is { } condition && !ConditionHolds(condition, resolver))
        {
            return new SkipJob(job.Id, $"Its condition is false: {condition.Trim()}");
        }

        return job switch
        {
            SwitchJob switchJob => ResolveSwitch(switchJob, resolver),
            JoinJob => new CompleteJoin(job.Id),
            _ => new StartJob(job.Id),
        };
    }

    /// <summary>A condition that does not parse never holds; the validator reports it before a run can start.</summary>
    private static bool ConditionHolds(string condition, IValueResolver resolver)
    {
        var (expression, _) = ExpressionSyntax.ParseCondition(condition);
        return expression is not null && ExpressionEvaluator.EvaluateCondition(expression, resolver);
    }

    private static CompleteSwitch ResolveSwitch(SwitchJob job, IValueResolver resolver)
    {
        var value = Template.Parse(job.Expression).Template.Render(resolver);
        var match = job.Cases.FirstOrDefault(c => !c.IsDefault && string.Equals(c.When, value, StringComparison.Ordinal))
            ?? job.Cases.FirstOrDefault(c => c.IsDefault);
        return new CompleteSwitch(job.Id, value, match?.Name);
    }

    private static JobState Apply(JobAction action, JobState state) => action switch
    {
        StartJob => state with { Status = JobStatus.Running },
        SkipJob => state with { Status = JobStatus.Skipped },
        CompleteSwitch resolved => state with { Status = JobStatus.Succeeded, ActivatedCase = resolved.ActivatedCase },
        CompleteJoin => state with { Status = JobStatus.Succeeded },
        _ => state with { Status = JobStatus.Cancelled },
    };

    private static string Describe(JobStatus status) => status switch
    {
        JobStatus.Succeeded => "succeeded",
        JobStatus.Failed => "failed",
        JobStatus.Cancelled => "was cancelled",
        _ => "was skipped",
    };

    private static string Describe(string? activatedCase) => activatedCase is null ? "no case" : $"case '{activatedCase}'";
}
