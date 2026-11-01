namespace AutoCanvas.Server.Application.Triggers;

/// <summary>One schedule entry.</summary>
/// <param name="Position">Its place under <c>schedule</c>, from 0.</param>
/// <param name="Schedule">How it reads: the cron expression, or <c>every 5m</c>.</param>
/// <param name="TimeZone">The time zone of a cron expression; <see langword="null"/> for UTC.</param>
/// <param name="NextAt">When it fires next; <see langword="null"/> if it never fires again.</param>
/// <param name="LastAt">When it last fired.</param>
public sealed record ScheduleView(int Position, string Schedule, string? TimeZone, DateTimeOffset? NextAt, DateTimeOffset? LastAt);
