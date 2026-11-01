using AutoCanvas.Persistence.Triggers;
using Cronos;

namespace AutoCanvas.Server.Application.Triggers;

/// <summary>When schedule entries fire: cron expressions with Cronos in their time zone, intervals by adding up.</summary>
internal static class ScheduleTimes
{
    /// <summary>The first time after <paramref name="after"/> the entry fires; <see langword="null"/> if it never does again.</summary>
    public static DateTimeOffset? Next(AutomationSchedule schedule, DateTimeOffset after)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.IntervalSeconds is { } seconds)
        {
            return after + TimeSpan.FromSeconds(seconds);
        }

        var zone = schedule.TimeZone is { } name ? TimeZoneInfo.FindSystemTimeZoneById(name) : TimeZoneInfo.Utc;
        return CronExpression.Parse(schedule.Cron!, CronFormat.Standard).GetNextOccurrence(after, zone);
    }

    /// <summary>Why a cron expression cannot be used, or <see langword="null"/>.</summary>
    public static string? CheckCron(string expression)
    {
        try
        {
            var cron = CronExpression.Parse(expression, CronFormat.Standard);
            return cron.GetNextOccurrence(DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc) is null
                ? $"'{expression}' never fires; check the day and month."
                : null;
        }
        catch (CronFormatException exception)
        {
            return $"'{expression}' is not a valid cron expression: {exception.Message}";
        }
    }

    /// <summary>Why a time zone cannot be used, or <see langword="null"/>.</summary>
    public static string? CheckTimeZone(string name)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(name);
            return null;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"'{name}' is not a known time zone. Use an IANA name such as Europe/Berlin.";
        }
    }

    /// <summary>How the entry reads, for <c>trigger.schedule</c> and the API.</summary>
    public static string Describe(AutomationSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.IntervalSeconds is not { } seconds)
        {
            return schedule.Cron!;
        }

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalDays >= 1 && span.TotalDays == Math.Floor(span.TotalDays) ? $"every {span.TotalDays}d"
            : span.TotalHours >= 1 && span.TotalHours == Math.Floor(span.TotalHours) ? $"every {span.TotalHours}h"
            : span.TotalMinutes >= 1 && span.TotalMinutes == Math.Floor(span.TotalMinutes) ? $"every {span.TotalMinutes}m"
            : $"every {seconds}s";
    }
}
