namespace AutoCanvas.Core.Orchestration;

/// <summary>No more steps: the job ends with <paramref name="Status"/>.</summary>
/// <param name="Status">Succeeded, Failed or Cancelled.</param>
public sealed record FinishJob(JobStatus Status) : StepDecision;
