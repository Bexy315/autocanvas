using AutoCanvas.Core.Orchestration;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>How a runner says a step ended (<c>StepCompleted</c>).</summary>
/// <param name="Index">Index of the step.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="ExitCode">Exit code of a script, if it exited.</param>
/// <param name="Outputs">Its outputs, masked by the runner.</param>
/// <param name="Error">Why it did not succeed.</param>
/// <param name="FinishedAt">When it ended, by the runner's clock.</param>
public sealed record StepReport(
    int Index,
    StepOutcome Outcome,
    int? ExitCode,
    IReadOnlyDictionary<string, string> Outputs,
    string? Error,
    DateTimeOffset? FinishedAt);
