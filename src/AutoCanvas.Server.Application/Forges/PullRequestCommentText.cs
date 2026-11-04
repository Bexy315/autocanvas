using System.Globalization;
using System.Text;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Reports;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// The text of the comment AutoCanvas keeps on a pull request: the latest run of each automation for
/// the pull request's head commit, with its tests and coverage.
/// </summary>
internal static class PullRequestCommentText
{
    /// <summary>The first line of every such comment, so that people (and AutoCanvas) recognize it.</summary>
    public const string Marker = "<!-- autocanvas -->";

    /// <summary>One automation's row.</summary>
    /// <param name="Automation">The automation's name.</param>
    /// <param name="Number">The run's number.</param>
    /// <param name="State">How the run stands.</param>
    /// <param name="Link">The run's page, if the instance has a public address.</param>
    /// <param name="Reports">Its test and coverage totals.</param>
    public sealed record Row(string Automation, int Number, RunState State, Uri? Link, RunReportsView Reports);

    /// <summary>The comment for the commit with these rows, by automation.</summary>
    public static string Of(string commit, IReadOnlyList<Row> rows)
    {
        var text = new StringBuilder();
        text.AppendLine(Marker);
        text.AppendLine("### AutoCanvas");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Runs for {commit[..Math.Min(7, commit.Length)]}:");
        text.AppendLine();
        text.AppendLine("| Automation | Result | Tests | Coverage |");
        text.AppendLine("|---|---|---|---|");
        foreach (var row in rows.OrderBy(r => r.Automation, StringComparer.Ordinal))
        {
            var run = $"{Escape(row.Automation)} #{row.Number.ToString(CultureInfo.InvariantCulture)}";
            text.AppendLine(CultureInfo.InvariantCulture, $"| {(row.Link is null ? run : $"[{run}]({row.Link})")} | {Result(row.State)} | {Tests(row.Reports.Tests)} | {Coverage(row.Reports.Coverage)} |");
        }

        return text.ToString();
    }

    /// <summary>A sentence for a commit status about the run.</summary>
    public static string Description(RunState state, TestTotalsView? tests, TimeSpan? took) => state switch
    {
        RunState.Running => "Running",
        RunState.Succeeded => Join("Succeeded", took, tests is { Total: > 0 } ? $"{tests.Passed} tests passed" : null),
        RunState.Failed => Join("Failed", took, tests is { Failed: > 0 } ? $"{tests.Failed} of {tests.Total} tests failed" : null),
        _ => "Cancelled",
    };

    private static string Join(string result, TimeSpan? took, string? tests) =>
        string.Join(" · ", new[] { took is { } duration ? $"{result} in {Duration(duration)}" : result, tests }.OfType<string>());

    private static string Duration(TimeSpan took) =>
        took.TotalMinutes >= 1 ? $"{(int)took.TotalMinutes}m {took.Seconds}s" : $"{Math.Max(1, (int)took.TotalSeconds)}s";

    private static string Result(RunState state) => state switch
    {
        RunState.Running => "⏳ Running",
        RunState.Succeeded => "✅ Succeeded",
        RunState.Failed => "❌ Failed",
        _ => "⏹️ Cancelled",
    };

    private static string Tests(TestTotalsView? tests) =>
        tests is null ? "–" : string.Join(", ", new[]
        {
            $"{tests.Passed} passed",
            tests.Failed > 0 ? $"{tests.Failed} failed" : null,
            tests.Skipped > 0 ? $"{tests.Skipped} skipped" : null,
        }.OfType<string>());

    private static string Coverage(CoverageTotalsView? coverage) =>
        coverage?.LineRate is { } rate ? string.Create(CultureInfo.InvariantCulture, $"{rate * 100:0.0} % lines") : "–";

    /// <summary>A name in a table cell: no pipes or brackets that would break the table or a link.</summary>
    private static string Escape(string name) => name.Replace("|", "\\|", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
}
