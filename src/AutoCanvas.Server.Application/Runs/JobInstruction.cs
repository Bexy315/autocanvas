namespace AutoCanvas.Server.Application.Runs;

/// <summary>What the runner of an attempt is to do next.</summary>
/// <param name="AttemptId">The attempt; the protocol's <c>job_id</c>.</param>
public abstract record JobInstruction(Guid AttemptId);
