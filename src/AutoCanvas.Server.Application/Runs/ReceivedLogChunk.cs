namespace AutoCanvas.Server.Application.Runs;

/// <summary>A batch of log lines as a runner sends it (<c>LogChunk</c>).</summary>
/// <param name="Sequence">Position in the attempt's log, from 0.</param>
/// <param name="StepIndex">The step that wrote the lines; <see langword="null"/> for preparation and cleanup.</param>
/// <param name="Lines">The lines, in order.</param>
public sealed record ReceivedLogChunk(long Sequence, int? StepIndex, IReadOnlyList<LogLine> Lines);
