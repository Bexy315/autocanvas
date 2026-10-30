namespace AutoCanvas.Core.Orchestration;

/// <summary>Record step <paramref name="Index"/> as skipped and decide again.</summary>
/// <param name="Index">Position of the step in the job.</param>
/// <param name="Reason">Why it is skipped.</param>
public sealed record SkipStep(int Index, string Reason) : StepDecision;
