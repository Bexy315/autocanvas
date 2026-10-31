namespace AutoCanvas.Server.Application.Runs;

/// <summary>No more steps: collect reports, clean up, report back (<c>CompleteJob</c>).</summary>
/// <param name="AttemptId">The attempt.</param>
public sealed record CompleteJobInstruction(Guid AttemptId) : JobInstruction(AttemptId);
