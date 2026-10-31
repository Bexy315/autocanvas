namespace AutoCanvas.Server.Application.Runs;

/// <summary>How long runners hold jobs (configuration section <c>Queue</c>).</summary>
public sealed class QueueOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Queue";

    /// <summary>
    /// A runner holds an offered or running job this long after its last heartbeat; then the job is
    /// offered again or, if it was running, fails. Default: one minute.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
}
