using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>A run with its jobs.</summary>
/// <param name="Id">The run's id.</param>
/// <param name="Number">Counts the automation's runs from 1.</param>
/// <param name="AutomationId">The automation, unless it has been deleted since.</param>
/// <param name="AutomationName">The automation's name when the run started.</param>
/// <param name="AutomationVersion">The version that runs.</param>
/// <param name="FolderId">The folder the run belongs to.</param>
/// <param name="State">Running, Succeeded, Failed or Cancelled.</param>
/// <param name="Trigger">What started the run.</param>
/// <param name="TriggeredByName">Who or what started it.</param>
/// <param name="CancelRequested">Whether someone asked to cancel it.</param>
/// <param name="CancelledByName">Who asked to cancel it.</param>
/// <param name="CreatedAt">When it started.</param>
/// <param name="FinishedAt">When it finished.</param>
/// <param name="Jobs">Its jobs in the order of the automation file.</param>
/// <param name="Yaml">The automation file the run runs, as it was when the run started.</param>
/// <param name="Commit">The commit a repository automation ran at; <see langword="null"/> for a managed one.</param>
/// <param name="Ref">The branch or tag of that commit, such as <c>refs/heads/main</c>.</param>
public sealed record RunView(
    Guid Id,
    int Number,
    Guid? AutomationId,
    string AutomationName,
    int AutomationVersion,
    Guid FolderId,
    RunState State,
    RunTriggerKind Trigger,
    string TriggeredByName,
    bool CancelRequested,
    string? CancelledByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<RunJobView> Jobs,
    string Yaml,
    string? Commit = null,
    string? Ref = null);
