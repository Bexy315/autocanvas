namespace AutoCanvas.Runner.Docker;

/// <summary>A line a process wrote, without its line terminator.</summary>
/// <param name="IsError">Whether it went to standard error.</param>
/// <param name="Text">The line.</param>
internal sealed record OutputLine(bool IsError, string Text);
