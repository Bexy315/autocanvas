namespace AutoCanvas.Core.Model;

/// <summary>Starts runs on a cron schedule.</summary>
/// <param name="Expression">Five-field cron expression, as written.</param>
/// <param name="TimeZone">IANA time zone, or <see langword="null"/> for UTC.</param>
public sealed record CronSchedule(string Expression, string? TimeZone) : ScheduleTrigger;
