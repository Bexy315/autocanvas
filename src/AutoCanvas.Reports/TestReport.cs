namespace AutoCanvas.Reports;

/// <summary>The tests of one report file, normalized across formats.</summary>
/// <param name="Cases">The test results in report order; at most <see cref="ReportLimits.MaxCases"/>.</param>
/// <param name="Counts">Totals over all tests in the file.</param>
/// <param name="Truncated">Whether the file had more tests than <see cref="Cases"/> holds.</param>
public sealed record TestReport(IReadOnlyList<TestCaseResult> Cases, TestCounts Counts, bool Truncated);
