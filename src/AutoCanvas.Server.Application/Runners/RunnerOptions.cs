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

    /// <summary>
    /// The address runners reach the runner endpoint at, when it differs from what admins would
    /// guess (behind a proxy, another port). Shown in the commands to register a runner; optional.
    /// When set, it wins over the address kept in the instance settings (ADR-0020).
    /// </summary>
    public Uri? PublicUrl { get; set; }
}
