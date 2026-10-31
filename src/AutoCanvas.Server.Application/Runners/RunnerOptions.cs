namespace AutoCanvas.Server.Application.Runners;

/// <summary>How the server talks to runners (configuration section <c>Runners</c>).</summary>
public sealed class RunnerOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Runners";

    /// <summary>
    /// How often runners send a heartbeat. It must be well below <c>Queue:LeaseDuration</c> so that
    /// a late heartbeat does not lose a job. Default: 20 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);
}
