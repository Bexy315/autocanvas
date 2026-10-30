using AutoCanvas.Core.Expressions;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Orchestration;

/// <summary>
/// What a run came to, from its jobs. One rule serves both finishing a run and <c>${{ run.status }}</c>.
/// </summary>
/// <remarks>
/// Finally jobs are left out of what the main jobs came to: they run because of it. A finally job
/// that fails still fails the run — a report that never arrived is worth seeing — except in a
/// cancelled run, which stays cancelled.
/// </remarks>
public static class RunOutcome
{
    /// <summary>The run's status: <see cref="RunStatus.Running"/> until every job has finished.</summary>
    /// <param name="automation">The automation the run was started with.</param>
    /// <param name="state">The run as it stands.</param>
    /// <returns>The run's status.</returns>
    public static RunStatus Of(AutomationDefinition automation, RunState state)
    {
        var main = OfMainJobs(automation, state);
        if (!automation.Jobs.All(j => state.Of(j.Id).IsFinished))
        {
            return RunStatus.Running;
        }

        if (main is RunStatus.Cancelled or RunStatus.Failed)
        {
            return main;
        }

        return automation.Jobs.Any(j => IsFinally(j) && state.Of(j.Id).Status == JobStatus.Failed)
            ? RunStatus.Failed
            : RunStatus.Succeeded;
    }

    /// <summary>
    /// What the jobs that are not finally jobs came to — the value of <c>${{ run.status }}</c> for a
    /// finally job. A cancel decides it even when a job had failed before.
    /// </summary>
    /// <param name="automation">The automation the run was started with.</param>
    /// <param name="state">The run as it stands.</param>
    /// <returns>The status of the main jobs.</returns>
    public static RunStatus OfMainJobs(AutomationDefinition automation, RunState state)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(state);

        var main = automation.Jobs.Where(j => !IsFinally(j)).Select(j => state.Of(j.Id).Status).ToList();
        if (state.CancelRequested || main.Contains(JobStatus.Cancelled))
        {
            return RunStatus.Cancelled;
        }

        if (main.Contains(JobStatus.Failed))
        {
            return RunStatus.Failed;
        }

        return main.TrueForAll(s => s is JobStatus.Succeeded or JobStatus.Skipped) ? RunStatus.Succeeded : RunStatus.Running;
    }

    /// <summary>The word <c>${{ run.status }}</c> gives for a status.</summary>
    /// <param name="status">The run status.</param>
    /// <returns><c>running</c>, <c>succeeded</c>, <c>failed</c> or <c>cancelled</c>.</returns>
    public static string Describe(RunStatus status) => status switch
    {
        RunStatus.Succeeded => "succeeded",
        RunStatus.Failed => "failed",
        RunStatus.Cancelled => "cancelled",
        _ => "running",
    };

    /// <summary>
    /// Whether a finished run changed anything, by the automation's <c>changed_when</c>. Without
    /// one — or when it cannot be evaluated — the answer is yes: a broken expression must not
    /// quietly hide runs from the history.
    /// </summary>
    /// <param name="automation">The automation the run was started with.</param>
    /// <param name="values">Supplies values, including job outputs.</param>
    /// <returns>Whether the run counts as a change.</returns>
    public static bool Changed(AutomationDefinition automation, IValueResolver values)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(values);

        if (automation.ChangedWhen is not { } condition)
        {
            return true;
        }

        var (expression, _) = ExpressionSyntax.ParseCondition(condition);
        return expression is null || ExpressionEvaluator.EvaluateCondition(expression, values);
    }

    private static bool IsFinally(JobDefinition job) => job is StepJob { RunsFinally: true };
}
