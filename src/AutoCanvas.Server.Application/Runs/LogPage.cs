namespace AutoCanvas.Server.Application.Runs;

/// <summary>A part of a job attempt's log.</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="Chunks">The chunks after the requested sequence, in order.</param>
/// <param name="Next">Pass as <c>after</c> to read on; the last sequence returned, or the requested one.</param>
/// <param name="Missing">Sequences before the last one returned that never arrived (at most 100).</param>
/// <param name="IsComplete">The attempt has finished and nothing follows these chunks.</param>
/// <param name="IsTruncated">The log reached its limit and later lines were dropped.</param>
public sealed record LogPage(Guid AttemptId, IReadOnlyList<LogChunkView> Chunks, long Next, IReadOnlyList<long> Missing, bool IsComplete, bool IsTruncated);
