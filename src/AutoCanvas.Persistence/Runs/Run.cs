using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Persistence.Runs;

/// <summary>
/// One execution of an automation (ADR-0015). It keeps a copy of the file it runs, so it stays
/// readable when the automation changes or is deleted.
/// </summary>
public sealed class Run
{
    /// <summary>Column size of <see cref="TriggeredByName"/>.</summary>
    public const int MaxTriggeredByNameLength = 200;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The automation; empty once it is deleted.</summary>
    public Guid? AutomationId { get; set; }

    /// <summary>The folder the run belongs to; its roles decide who may see and cancel the run.</summary>
    public required Guid FolderId { get; set; }

    /// <summary>The automation's name when the run started.</summary>
    public required string AutomationName { get; init; }

    /// <summary>The automation version that runs.</summary>
    public required int AutomationVersion { get; init; }

    /// <summary>Counts the automation's runs from 1, for people; unique per automation.</summary>
    public required int Number { get; init; }

    /// <summary>The automation file as it was when the run started.</summary>
    public required string Yaml { get; init; }

    /// <summary>The commit a repository automation ran at; <c>checkout: self</c> gets it.</summary>
    public string? CommitSha { get; init; }

    /// <summary>The branch or tag that commit came from, such as <c>refs/heads/main</c>.</summary>
    public string? Ref { get; init; }

    /// <summary>The repository a repository automation's run comes from, to report back to; no foreign key, the run outlives it.</summary>
    public Guid? RepositoryId { get; init; }

    /// <summary>The number of the pull request the run is for, if a pull request started it.</summary>
    public int? PullRequest { get; init; }

    /// <summary>
    /// Whether the run gets no secrets: a pull request from a fork, whose code the folder's people
    /// have not seen. <c>secrets.*</c> read as empty, and other repositories are not checked out.
    /// </summary>
    public bool SecretsWithheld { get; init; }

    /// <summary>Where the run stands.</summary>
    public RunState State { get; set; } = RunState.Running;

    /// <summary>What started the run.</summary>
    public required RunTriggerKind Trigger { get; init; }

    /// <summary>Facts of the trigger as a small JSON object (e.g. a webhook's body); readable as <c>trigger.*</c>.</summary>
    public string? TriggerData { get; init; }

    /// <summary>The user who started the run, if a user did.</summary>
    public Guid? TriggeredById { get; init; }

    /// <summary>Who or what started the run, as shown: a user name, "schedule", "webhook".</summary>
    public required string TriggeredByName { get; init; }

    /// <summary>Whether someone asked to cancel the run.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>Who asked to cancel the run.</summary>
    public string? CancelledByName { get; set; }

    /// <summary>When the run was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the run reached its final state.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// Whether the automations that start when this one finishes (<c>automation_completed</c>) have
    /// been looked at; set once by whichever server takes the finished run first.
    /// </summary>
    public bool CompletionHandled { get; set; }

    /// <summary>Navigation to the automation.</summary>
    public Automation? Automation { get; init; }

    /// <summary>Navigation to the folder.</summary>
    public Folder? Folder { get; init; }
}
