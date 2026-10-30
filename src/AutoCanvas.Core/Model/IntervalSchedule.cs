namespace AutoCanvas.Core.Model;

/// <summary>Starts runs at a fixed interval.</summary>
/// <param name="Interval">Time between runs.</param>
public sealed record IntervalSchedule(TimeSpan Interval) : ScheduleTrigger;
