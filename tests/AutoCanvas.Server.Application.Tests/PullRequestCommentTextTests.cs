using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Reports;

namespace AutoCanvas.Server.Application.Tests;

public sealed class PullRequestCommentTextTests
{
    private static readonly RunReportsView NoReports = new(null, null, []);

    [Fact]
    public void The_comment_has_a_row_per_automation_with_its_result_tests_and_coverage()
    {
        var text = PullRequestCommentText.Of("0123456789abcdef", [
            new("test", 4, RunState.Succeeded, null, new RunReportsView(new TestTotalsView(10, 9, 0, 1, 100), new CoverageTotalsView(843, 1000, 0, 0), [])),
            new("build|all", 3, RunState.Failed, new Uri("https://autocanvas.example.org/runs/1"), new RunReportsView(new TestTotalsView(5, 3, 2, 0, 50), null, [])),
            new("lint", 1, RunState.Running, null, NoReports),
            new("docs", 2, RunState.Cancelled, null, NoReports),
        ]);

        text.ShouldStartWith(PullRequestCommentText.Marker);
        text.ShouldContain("Runs for 0123456:");
        text.ShouldContain("| [build\\|all #3](https://autocanvas.example.org/runs/1) | ❌ Failed | 3 passed, 2 failed | – |");
        text.ShouldContain("| test #4 | ✅ Succeeded | 9 passed, 1 skipped | 84.3 % lines |");
        text.ShouldContain("| lint #1 | ⏳ Running | – | – |");
        text.ShouldContain("| docs #2 | ⏹️ Cancelled | – | – |");
        text.IndexOf("build", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("docs #2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RunState.Running, null, null, "Running")]
    [InlineData(RunState.Succeeded, 130, 12, "Succeeded in 2m 10s · 12 tests passed")]
    [InlineData(RunState.Succeeded, 0, null, "Succeeded in 1s")]
    [InlineData(RunState.Failed, 42, 3, "Failed in 42s · 3 of 12 tests failed")]
    [InlineData(RunState.Failed, null, null, "Failed")]
    [InlineData(RunState.Cancelled, 5, null, "Cancelled")]
    public void A_status_says_how_the_run_went_in_a_sentence(RunState state, int? seconds, int? tests, string description)
    {
        var totals = tests is null ? null : state == RunState.Failed ? new TestTotalsView(12, 12 - tests.Value, tests.Value, 0, 1) : new TestTotalsView(tests.Value, tests.Value, 0, 0, 1);

        PullRequestCommentText.Description(state, totals, seconds is null ? null : TimeSpan.FromSeconds(seconds.Value)).ShouldBe(description);
    }
}
