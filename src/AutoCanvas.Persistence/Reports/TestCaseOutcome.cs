namespace AutoCanvas.Persistence.Reports;

/// <summary>How a stored test ended.</summary>
public enum TestCaseOutcome
{
    /// <summary>It passed, possibly after reruns.</summary>
    Passed,

    /// <summary>It failed or errored.</summary>
    Failed,

    /// <summary>It did not run.</summary>
    Skipped,
}
