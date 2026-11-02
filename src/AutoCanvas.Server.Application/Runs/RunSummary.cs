using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>A run as listed.</summary>
/// <param name="Id">The run's id.</param>
/// <param name="Number">Counts the automation's runs from 1.</param>
/// <param name="AutomationId">The automation, unless it has been deleted since.</param>
/// <param name="AutomationName">The automation's name when the run started.</param>
/// <param name="FolderId">The folder the run belongs to.</param>
/// <param name="State">Running, Succeeded, Failed or Cancelled.</param>
/// <param name="Trigger">What started the run.</param>
/// <param name="TriggeredByName">Who or what started it.</param>
/// <param name="CreatedAt">When it started.</param>
/// <param name="FinishedAt">When it finished.</param>
public sealed record RunSummary(
    Guid Id,
    int Number,
    Guid? AutomationId,
    string AutomationName,
    Guid FolderId,
    RunState State,
    RunTriggerKind Trigger,
    string TriggeredByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);
