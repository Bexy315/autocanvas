namespace AutoCanvas.Server.Application.Runs;

/// <summary>How long runs are kept (configuration section <c>Runs</c>).</summary>
public sealed class RunOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Runs";

    /// <summary>
    /// Finished runs older than this many days are deleted once a day, with their logs and reports;
    /// <c>0</c> keeps them forever. Default: 90.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>The newest runs of each automation that are kept however old they are. Default: 20.</summary>
    public int KeepPerAutomation { get; set; } = 20;
}
