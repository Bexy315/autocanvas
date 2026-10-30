namespace AutoCanvas.Core.Orchestration;

/// <summary>The job's needs are met and its condition holds: queue it for a runner.</summary>
/// <param name="JobId">The job.</param>
public sealed record StartJob(string JobId) : JobAction(JobId);
