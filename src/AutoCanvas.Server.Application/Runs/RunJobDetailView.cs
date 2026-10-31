using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>A job of a run with its outputs and every attempt.</summary>
/// <param name="Key">The job's id in the automation file.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Status">Pending, Running, Succeeded, Failed, Cancelled or Skipped.</param>
/// <param name="Reason">Why it was skipped, or what a switch decided.</param>
/// <param name="Outputs">What it published under <c>outputs</c>, masked.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="FinishedAt">When it finished.</param>
/// <param name="Attempts">Its attempts, first first.</param>
public sealed record RunJobDetailView(
    string Key,
    string Name,
    RunJobStatus Status,
    string? Reason,
    IReadOnlyDictionary<string, string> Outputs,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<JobAttemptView> Attempts);
