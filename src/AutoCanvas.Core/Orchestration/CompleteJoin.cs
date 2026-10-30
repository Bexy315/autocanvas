namespace AutoCanvas.Core.Orchestration;

/// <summary>A join's needs are met; mark it succeeded.</summary>
/// <param name="JobId">The join job.</param>
public sealed record CompleteJoin(string JobId) : JobAction(JobId);
