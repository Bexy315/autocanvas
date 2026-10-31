using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>A step of a job attempt, as far as it got.</summary>
/// <param name="Index">Its position in the job, from 0.</param>
/// <param name="StepId">Its id in the automation, if it has one.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Status">Sent, Running, Succeeded, Failed, Cancelled, TimedOut, Errored or Skipped.</param>
/// <param name="Reason">Why it was skipped.</param>
/// <param name="ExitCode">Exit code of a script step.</param>
/// <param name="Outputs">Its outputs, masked.</param>
/// <param name="Error">What went wrong.</param>
/// <param name="StartedAt">When it started, by the runner's clock.</param>
/// <param name="FinishedAt">When it ended, by the runner's clock.</param>
public sealed record JobStepView(
    int Index,
    string? StepId,
    string Name,
    JobStepStatus Status,
    string? Reason,
    int? ExitCode,
    IReadOnlyDictionary<string, string> Outputs,
    string? Error,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);
