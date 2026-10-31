namespace AutoCanvas.Server.Application.Reports;

/// <summary>How a test went in an automation's recent runs.</summary>
/// <param name="FullName">The test, <c>Suite.Name</c>.</param>
/// <param name="Passed">How often it passed among <paramref name="Entries"/>.</param>
/// <param name="Failed">How often it failed among them.</param>
/// <param name="Skipped">How often it did not run among them.</param>
/// <param name="Entries">Its results, newest first; a test reported twice in one attempt appears twice.</param>
public sealed record TestHistoryView(string FullName, int Passed, int Failed, int Skipped, IReadOnlyList<TestHistoryEntry> Entries);
