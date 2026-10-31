namespace AutoCanvas.Server.Application.Reports;

/// <summary>Counts of tests, over every test even when a report's details were cut.</summary>
/// <param name="Total">All tests.</param>
/// <param name="Passed">Tests that passed.</param>
/// <param name="Failed">Tests that failed.</param>
/// <param name="Skipped">Tests that did not run.</param>
/// <param name="DurationMs">Sum of the tests' durations in milliseconds.</param>
public sealed record TestTotalsView(int Total, int Passed, int Failed, int Skipped, long DurationMs)
{
    /// <summary>The sum of two counts.</summary>
    /// <param name="other">The counts to add.</param>
    /// <returns>The combined counts.</returns>
    public TestTotalsView Add(TestTotalsView other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(Total + other.Total, Passed + other.Passed, Failed + other.Failed, Skipped + other.Skipped, DurationMs + other.DurationMs);
    }
}
