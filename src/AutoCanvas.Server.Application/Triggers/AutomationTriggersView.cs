namespace AutoCanvas.Server.Application.Triggers;

/// <summary>What starts runs of an automation, as its current version says.</summary>
/// <param name="Manual">Whether people may start it by hand.</param>
/// <param name="Webhook">Whether a webhook call may start it.</param>
/// <param name="Schedules">Its schedule entries with when each fires next.</param>
/// <param name="Follows">The automation whose finished runs start it, if any.</param>
public sealed record AutomationTriggersView(bool Manual, bool Webhook, IReadOnlyList<ScheduleView> Schedules, CompletionView? Follows);
