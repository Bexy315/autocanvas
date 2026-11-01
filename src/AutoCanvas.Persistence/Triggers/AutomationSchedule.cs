namespace AutoCanvas.Persistence.Triggers;

/// <summary>
/// One <c>schedule</c> entry of an automation's current version, with the next time it fires. The
/// trigger service takes a due entry by moving <see cref="NextAt"/> on, so that of several servers
/// only one starts the run.
/// </summary>
public sealed class AutomationSchedule
{
    /// <summary>Column size of <see cref="Cron"/> and <see cref="TimeZone"/>.</summary>
    public const int MaxTextLength = 100;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The automation.</summary>
    public required Guid AutomationId { get; init; }

    /// <summary>Position of the entry under <c>schedule</c>, from 0.</summary>
    public required int Position { get; set; }

    /// <summary>The five-field cron expression; <see langword="null"/> for an interval.</summary>
    public string? Cron { get; init; }

    /// <summary>The IANA time zone of <see cref="Cron"/>; <see langword="null"/> for UTC.</summary>
    public string? TimeZone { get; init; }

    /// <summary>The interval in seconds; <see langword="null"/> for a cron expression.</summary>
    public long? IntervalSeconds { get; init; }

    /// <summary>When it fires next.</summary>
    public required DateTimeOffset NextAt { get; set; }

    /// <summary>When it last fired.</summary>
    public DateTimeOffset? LastAt { get; set; }

    /// <summary>Navigation to the automation.</summary>
    public Automations.Automation? Automation { get; init; }
}
