namespace AutoCanvas.Server.Application.Runs;

/// <summary>Limits of job logs (configuration section <c>Logs</c>).</summary>
public sealed class LogOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Logs";

    /// <summary>
    /// How many bytes of log text one job attempt may store; later lines are dropped and the log
    /// says so. Default: 64 MiB.
    /// </summary>
    public long MaxBytesPerJob { get; set; } = 64L * 1024 * 1024;
}
