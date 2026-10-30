namespace AutoCanvas.Core.Orchestration;

/// <summary>What happens next in a job: run a step, skip one, or finish the job.</summary>
public abstract record StepDecision;
