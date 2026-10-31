namespace AutoCanvas.Server.Application.Runs;

/// <summary>Where a log line came from.</summary>
public enum LogSource
{
    /// <summary>Standard output of a step or of preparation.</summary>
    Stdout,

    /// <summary>Standard error.</summary>
    Stderr,

    /// <summary>Written by the runner or the server itself, such as "Pulling image".</summary>
    System,
}
