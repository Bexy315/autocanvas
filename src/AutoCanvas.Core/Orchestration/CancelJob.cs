namespace AutoCanvas.Core.Orchestration;

/// <summary>Stop the job (telling its runner if it is running) and mark it cancelled.</summary>
/// <param name="JobId">The job.</param>
public sealed record CancelJob(string JobId) : JobAction(JobId);
