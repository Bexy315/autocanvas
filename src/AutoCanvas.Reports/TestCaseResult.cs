namespace AutoCanvas.Reports;

/// <summary>One test result from a report. Long texts are cut to <see cref="ReportLimits"/>.</summary>
/// <param name="Group">Where the test comes from, when the format says: the assembly (TRX) or the enclosing test suite (JUnit).</param>
/// <param name="Suite">The test class or suite the test belongs to.</param>
/// <param name="Name">The test's name within <paramref name="Suite"/>, including data-row arguments where the format has them.</param>
/// <param name="Outcome">How the test ended.</param>
/// <param name="Duration">How long it ran; zero when the report does not say.</param>
/// <param name="Message">Why it failed or was skipped, or a note such as a pass after reruns.</param>
/// <param name="StackTrace">The failure's stack trace or full failure text.</param>
/// <param name="Output">What the test wrote to standard output and error.</param>
public sealed record TestCaseResult(
    string? Group,
    string Suite,
    string Name,
    TestOutcome Outcome,
    TimeSpan Duration,
    string? Message,
    string? StackTrace,
    string? Output)
{
    /// <summary>
    /// The test's identity across runs, <c>Suite.Name</c> (or just the name without a suite); the
    /// history of a test is keyed by it.
    /// </summary>
    public string FullName => Suite.Length == 0 ? Name : $"{Suite}.{Name}";
}
