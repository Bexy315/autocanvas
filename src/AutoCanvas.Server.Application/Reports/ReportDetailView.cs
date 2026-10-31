namespace AutoCanvas.Server.Application.Reports;

/// <summary>A report with its packages, for coverage reports.</summary>
/// <param name="Report">The report and its totals.</param>
/// <param name="Packages">Its packages in report order; empty for test reports.</param>
public sealed record ReportDetailView(ReportSummaryView Report, IReadOnlyList<CoveragePackageView> Packages);
