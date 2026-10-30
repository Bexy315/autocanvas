namespace AutoCanvas.Reports;

/// <summary>Totals of a test report, over every test in the file even when the list of cases was cut.</summary>
/// <param name="Total">All tests.</param>
/// <param name="Passed">Tests that passed.</param>
/// <param name="Failed">Tests that failed.</param>
/// <param name="Skipped">Tests that did not run.</param>
/// <param name="Duration">Sum of the tests' durations.</param>
public sealed record TestCounts(int Total, int Passed, int Failed, int Skipped, TimeSpan Duration);
