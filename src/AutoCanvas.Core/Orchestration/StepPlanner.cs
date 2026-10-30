using AutoCanvas.Core.Expressions;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Orchestration;

/// <summary>
/// Decides, step by step, how a job proceeds (ADR-0008: the server resolves steps one at a time).
/// After a failure, the remaining steps are skipped; cleanup that must always happen belongs in a
/// <c>finally</c> job.
/// </summary>
public static class StepPlanner
{
    /// <summary>The next decision for <paramref name="job"/>, given the outcomes of its steps so far.</summary>
    /// <param name="job">The job.</param>
    /// <param name="outcomes">Outcomes of the steps already decided, in order: one per step from index 0.</param>
    /// <param name="values">Supplies values for step conditions, including earlier steps' outputs.</param>
    /// <returns>Run or skip the next step, or finish the job.</returns>
    public static StepDecision Next(StepJob job, IReadOnlyList<StepOutcome> outcomes, IValueResolver values)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(values);

        var index = outcomes.Count;
        if (index >= job.Steps.Count)
        {
            return new FinishJob(JobResult(job, outcomes));
        }

        if (Stopped(job, outcomes) is { } reason)
        {
            return new SkipStep(index, reason);
        }

        var step = job.Steps[index];
        if (step.Condition is { } condition && !ConditionHolds(condition, values))
        {
            return new SkipStep(index, $"Its condition is false: {condition.Trim()}");
        }

        return new RunStep(index);
    }

    /// <summary>The job's result from its step outcomes: cancelled, failed, or succeeded.</summary>
    /// <param name="job">The job.</param>
    /// <param name="outcomes">Outcomes of its steps, in order.</param>
    /// <returns>The job's status.</returns>
    public static JobStatus JobResult(StepJob job, IReadOnlyList<StepOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(outcomes);

        if (outcomes.Contains(StepOutcome.Cancelled))
        {
            return JobStatus.Cancelled;
        }

        return Enumerable.Range(0, outcomes.Count).Any(i => Fails(job.Steps[i], outcomes[i]))
            ? JobStatus.Failed
            : JobStatus.Succeeded;
    }

    /// <summary>
    /// Whether this outcome fails the job. <c>continue_on_error</c> forgives a failing or timed-out
    /// step, but not a runner error — that is no verdict on the step.
    /// </summary>
    private static bool Fails(StepDefinition step, StepOutcome outcome) => outcome switch
    {
        StepOutcome.Failed or StepOutcome.TimedOut => !step.ContinueOnError,
        StepOutcome.Errored => true,
        _ => false,
    };

    private static string? Stopped(StepJob job, IReadOnlyList<StepOutcome> outcomes)
    {
        for (var i = 0; i < outcomes.Count; i++)
        {
            if (outcomes[i] == StepOutcome.Cancelled)
            {
                return "The job was cancelled.";
            }

            if (Fails(job.Steps[i], outcomes[i]))
            {
                return $"Step {i + 1} ({job.Steps[i].Name ?? job.Steps[i].Id ?? "unnamed"}) failed.";
            }
        }

        return null;
    }

    private static bool ConditionHolds(string condition, IValueResolver resolver)
    {
        var (expression, _) = ExpressionSyntax.ParseCondition(condition);
        return expression is not null && ExpressionEvaluator.EvaluateCondition(expression, resolver);
    }
}
