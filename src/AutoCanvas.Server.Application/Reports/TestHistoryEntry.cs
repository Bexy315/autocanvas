using AutoCanvas.Persistence.Reports;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>One result of a test in a run.</summary>
/// <param name="RunId">The run.</param>
/// <param name="RunNumber">The run's number in its automation.</param>
/// <param name="JobKey">The job.</param>
/// <param name="AttemptId">The attempt.</param>
/// <param name="Attempt">The attempt's number, from 1.</param>
/// <param name="ReportId">The report the result is in.</param>
/// <param name="Outcome">Passed, Failed or Skipped.</param>
/// <param name="DurationMs">How long it ran, in milliseconds.</param>
/// <param name="Message">Why it failed or was skipped.</param>
/// <param name="ReportedAt">When the report arrived.</param>
public sealed record TestHistoryEntry(
    Guid RunId,
    int RunNumber,
    string JobKey,
    Guid AttemptId,
    int Attempt,
    Guid ReportId,
    TestCaseOutcome Outcome,
    long DurationMs,
    string? Message,
    DateTimeOffset ReportedAt);
