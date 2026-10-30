namespace AutoCanvas.Core.Orchestration;

/// <summary>A switch was evaluated; mark it succeeded with the case it activated.</summary>
/// <param name="JobId">The switch job.</param>
/// <param name="Value">What its expression evaluated to.</param>
/// <param name="ActivatedCase">The matching case, the default case, or <see langword="null"/> when nothing matched and there is no default.</param>
public sealed record CompleteSwitch(string JobId, string Value, string? ActivatedCase) : JobAction(JobId);
