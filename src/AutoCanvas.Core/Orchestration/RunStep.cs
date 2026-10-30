namespace AutoCanvas.Core.Orchestration;

/// <summary>Resolve step <paramref name="Index"/> and send it to the runner.</summary>
/// <param name="Index">Position of the step in the job.</param>
public sealed record RunStep(int Index) : StepDecision;
