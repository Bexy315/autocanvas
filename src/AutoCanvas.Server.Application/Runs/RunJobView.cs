using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>A job of a run.</summary>
/// <param name="Key">The job's id in the automation file.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Status">Pending, Running, Succeeded, Failed, Cancelled or Skipped.</param>
/// <param name="ActivatedCase">For a switch job: the case it chose.</param>
/// <param name="Reason">Why it was skipped, or what a switch decided.</param>
/// <param name="Attempts">How many attempts it has had.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="FinishedAt">When it finished.</param>
public sealed record RunJobView(
    string Key,
    string Name,
    RunJobStatus Status,
    string? ActivatedCase,
    string? Reason,
    int Attempts,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);
