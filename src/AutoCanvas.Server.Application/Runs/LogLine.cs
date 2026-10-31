namespace AutoCanvas.Server.Application.Runs;

/// <summary>One line of a job's log.</summary>
/// <param name="Time">When it was written, by the runner's clock.</param>
/// <param name="Source">Where it came from: standard output, standard error or the runner itself.</param>
/// <param name="Text">The text without its line ending, masked.</param>
public sealed record LogLine(DateTimeOffset Time, LogSource Source, string Text);
