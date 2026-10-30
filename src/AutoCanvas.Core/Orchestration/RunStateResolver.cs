using AutoCanvas.Core.Expressions;

namespace AutoCanvas.Core.Orchestration;

/// <summary>
/// Answers <c>jobs.JOB.result</c> from the planner's working state and everything else from the
/// caller's resolver, so conditions see decisions made earlier in the same planning call.
/// </summary>
internal sealed class RunStateResolver(IReadOnlyDictionary<string, JobState> jobs, IValueResolver values) : IValueResolver
{
    public bool TryResolve(IReadOnlyList<string> path, out string? value)
    {
        if (path is ["jobs", var job, "result"] && jobs.TryGetValue(job, out var state) && state.IsFinished)
        {
            value = Result(state.Status);
            return true;
        }

        return values.TryResolve(path, out value);
    }

    /// <summary>The word <c>${{ jobs.JOB.result }}</c> gives for a finished job.</summary>
    public static string Result(JobStatus status) => status switch
    {
        JobStatus.Succeeded => "success",
        JobStatus.Failed => "failure",
        JobStatus.Cancelled => "cancelled",
        _ => "skipped",
    };
}
