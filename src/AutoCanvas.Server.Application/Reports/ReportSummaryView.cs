using System.Text.Json;
using AutoCanvas.Persistence.Reports;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>A report file of a job attempt with its totals.</summary>
/// <param name="Id">The report's id.</param>
/// <param name="JobKey">The job.</param>
/// <param name="AttemptId">The attempt that uploaded it.</param>
/// <param name="Name">Its label in the automation.</param>
/// <param name="Format"><c>junit</c>, <c>trx</c> or <c>cobertura</c>.</param>
/// <param name="Kind">Tests or Coverage.</param>
/// <param name="Path">The file's path in the workspace.</param>
/// <param name="Size">Its size in bytes, as the runner reported it.</param>
/// <param name="ReceivedAt">When the server stored it.</param>
/// <param name="Tests">Counts of a test report that was read.</param>
/// <param name="Coverage">Totals of a coverage report that was read.</param>
/// <param name="Error">Why the file could not be read.</param>
/// <param name="Warnings">What was read, but not completely.</param>
/// <param name="Truncated">Details were cut at the limits; the totals count everything.</param>
public sealed record ReportSummaryView(
    Guid Id,
    string JobKey,
    Guid AttemptId,
    string Name,
    string Format,
    ReportKind Kind,
    string Path,
    long Size,
    DateTimeOffset ReceivedAt,
    TestTotalsView? Tests,
    CoverageTotalsView? Coverage,
    string? Error,
    IReadOnlyList<string> Warnings,
    bool Truncated)
{
    internal static ReportSummaryView Of(Report report) => new(
        report.Id,
        report.JobKey,
        report.AttemptId,
        report.Name,
        report.Format,
        report.Kind,
        report.Path,
        report.Size,
        report.ReceivedAt,
        report.TestsTotal is { } total
            ? new TestTotalsView(total, report.TestsPassed ?? 0, report.TestsFailed ?? 0, report.TestsSkipped ?? 0, report.TestsDurationMs ?? 0)
            : null,
        report.LinesValid is { } lines
            ? new CoverageTotalsView(report.LinesCovered ?? 0, lines, report.BranchesCovered ?? 0, report.BranchesValid ?? 0)
            : null,
        report.Error,
        report.Warnings is null ? [] : JsonSerializer.Deserialize<string[]>(report.Warnings) ?? [],
        report.Truncated);
}
