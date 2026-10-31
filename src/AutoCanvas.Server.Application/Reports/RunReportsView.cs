namespace AutoCanvas.Server.Application.Reports;

/// <summary>The reports of a run.</summary>
/// <param name="Tests">
/// Counts over the test reports of each job's latest attempt that uploaded any, so that a retried
/// job counts once; <see langword="null"/> without test reports.
/// </param>
/// <param name="Coverage">Totals over the coverage reports, chosen the same way.</param>
/// <param name="Reports">Every report of every attempt, by job and then as received.</param>
public sealed record RunReportsView(TestTotalsView? Tests, CoverageTotalsView? Coverage, IReadOnlyList<ReportSummaryView> Reports);
