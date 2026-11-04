using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>What starts a run.</summary>
/// <param name="Trigger">The kind of trigger, readable as <c>trigger.event</c>.</param>
/// <param name="TriggeredById">The person who started it, if one did.</param>
/// <param name="TriggeredByName">Who or what started it, for display.</param>
/// <param name="Data">The trigger's details as a JSON object, readable as <c>trigger.*</c>.</param>
internal sealed record RunStart(RunTriggerKind Trigger, Guid? TriggeredById, string TriggeredByName, string? Data = null)
{
    /// <summary>The commit to run at, for a repository automation; its current version's when unset.</summary>
    public string? Commit { get; init; }

    /// <summary>The branch or tag the commit came from; the repository's branch when unset.</summary>
    public string? Ref { get; init; }

    /// <summary>The pull request the run is for, if one started it.</summary>
    public int? PullRequest { get; init; }

    /// <summary>Whether the run gets no secrets (a pull request from a fork).</summary>
    public bool SecretsWithheld { get; init; }
}
