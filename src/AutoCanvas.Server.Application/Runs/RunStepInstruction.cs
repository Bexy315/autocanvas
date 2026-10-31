namespace AutoCanvas.Server.Application.Runs;

/// <summary>Execute this step (<c>RunStep</c>).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="Step">The step, resolved.</param>
public sealed record RunStepInstruction(Guid AttemptId, ResolvedStep Step) : JobInstruction(AttemptId);
