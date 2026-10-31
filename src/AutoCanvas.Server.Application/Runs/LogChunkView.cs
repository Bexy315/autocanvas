namespace AutoCanvas.Server.Application.Runs;

/// <summary>A chunk of a job's log.</summary>
/// <param name="Sequence">Its position in the log; numbers that are missing were never received.</param>
/// <param name="StepIndex">The step that wrote it; <see langword="null"/> for preparation and cleanup.</param>
/// <param name="Lines">Its lines, masked.</param>
public sealed record LogChunkView(long Sequence, int? StepIndex, IReadOnlyList<LogLine> Lines);
