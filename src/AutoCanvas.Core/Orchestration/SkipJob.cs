namespace AutoCanvas.Core.Orchestration;

/// <summary>The job will not run; mark it skipped.</summary>
/// <param name="JobId">The job.</param>
/// <param name="Reason">Why, for the run view, such as "'build' failed, but this job needs it to have succeeded."</param>
public sealed record SkipJob(string JobId, string Reason) : JobAction(JobId);
