using AutoCanvas.Persistence.Triggers;

namespace AutoCanvas.Server.Application.Triggers;

/// <summary>The checked triggers of a file, ready to be stored by <see cref="TriggerIndex.ApplyAsync"/>.</summary>
/// <param name="Schedules">The schedule entries, in order; their ids and times are set when stored.</param>
/// <param name="Completion">What the automation follows, if anything.</param>
internal sealed record TriggerPlan(IReadOnlyList<AutomationSchedule> Schedules, CompletionTrigger? Completion);
