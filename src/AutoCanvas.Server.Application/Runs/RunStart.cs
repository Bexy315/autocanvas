using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>What starts a run.</summary>
/// <param name="Trigger">The kind of trigger, readable as <c>trigger.event</c>.</param>
/// <param name="TriggeredById">The person who started it, if one did.</param>
/// <param name="TriggeredByName">Who or what started it, for display.</param>
/// <param name="Data">The trigger's details as a JSON object, readable as <c>trigger.*</c>.</param>
internal sealed record RunStart(RunTriggerKind Trigger, Guid? TriggeredById, string TriggeredByName, string? Data = null);
