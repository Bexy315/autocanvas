using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>One try to run a job.</summary>
/// <param name="Id">The attempt's id; its log is read by it.</param>
/// <param name="Number">Attempt number, from 1.</param>
/// <param name="Status">Queued, Offered, Running, Succeeded, Failed or Cancelled.</param>
/// <param name="RunnerId">The runner that holds or held it.</param>
/// <param name="RunnerName">That runner's name, while it exists.</param>
/// <param name="QueuedAt">When it was queued.</param>
/// <param name="StartedAt">When a runner accepted it.</param>
/// <param name="FinishedAt">When it ended.</param>
/// <param name="Error">Why it failed or was cancelled.</param>
/// <param name="Warnings">Problems the runner had collecting reports or cleaning up.</param>
/// <param name="Steps">Its steps, as far as it got.</param>
public sealed record JobAttemptView(
    Guid Id,
    int Number,
    AttemptStatus Status,
    Guid? RunnerId,
    string? RunnerName,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<JobStepView> Steps);
