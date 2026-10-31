namespace AutoCanvas.Server.Application.Audit;

/// <summary>How long the audit log is kept (configuration section <c>Audit</c>).</summary>
public sealed class AuditOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Audit";

    /// <summary>
    /// Entries older than this many days are deleted once a day; <c>0</c> keeps them forever.
    /// Default: 365.
    /// </summary>
    public int RetentionDays { get; set; } = 365;
}
