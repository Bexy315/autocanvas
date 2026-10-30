namespace AutoCanvas.Reports;

/// <summary>How a single test ended.</summary>
public enum TestOutcome
{
    /// <summary>The test passed, possibly after reruns (see <see cref="TestCaseResult.Message"/>).</summary>
    Passed,

    /// <summary>The test failed or errored.</summary>
    Failed,

    /// <summary>The test did not run: skipped, ignored, not executed or inconclusive.</summary>
    Skipped,
}
