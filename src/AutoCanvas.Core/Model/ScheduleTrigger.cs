namespace AutoCanvas.Core.Model;

/// <summary>A time-based trigger: either a <see cref="CronSchedule"/> or an <see cref="IntervalSchedule"/>.</summary>
public abstract record ScheduleTrigger;
