using AutoCanvas.Persistence.Reports;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>One test of a test report.</summary>
/// <param name="Index">Its position in the report, from 0.</param>
/// <param name="Group">Assembly (TRX) or enclosing suite (JUnit), when the format says.</param>
/// <param name="Suite">The test class or suite.</param>
/// <param name="Name">The test's name within its suite.</param>
/// <param name="FullName"><c>Suite.Name</c>, which the history of the test follows.</param>
/// <param name="Outcome">Passed, Failed or Skipped.</param>
/// <param name="DurationMs">How long it ran, in milliseconds.</param>
/// <param name="Message">Why it failed or was skipped.</param>
/// <param name="StackTrace">The failure's stack trace.</param>
/// <param name="Output">What it wrote to standard output and error.</param>
public sealed record TestCaseView(
    int Index,
    string? Group,
    string Suite,
    string Name,
    string FullName,
    TestCaseOutcome Outcome,
    long DurationMs,
    string? Message,
    string? StackTrace,
    string? Output);
