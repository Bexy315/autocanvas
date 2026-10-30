using AutoCanvas.Core.Orchestration;
using AutoCanvas.Core.Tests.Expressions;
using static AutoCanvas.Core.Tests.Orchestration.RunPlannerTests;

namespace AutoCanvas.Core.Tests.Orchestration;

public sealed class RunOutcomeTests
{
    private const string Automation = """
        version: 1
        changed_when: jobs.check.outputs.changed
        jobs:
          check: { outputs: { changed: x }, steps: [ { run: a } ] }
          fix: { needs: [check], steps: [ { run: a } ] }
          report: { finally: true, steps: [ { run: a } ] }
        """;

    [Theory]
    [InlineData(JobStatus.Running, JobStatus.Pending, JobStatus.Pending, RunStatus.Running)]
    [InlineData(JobStatus.Succeeded, JobStatus.Succeeded, JobStatus.Running, RunStatus.Running)]
    [InlineData(JobStatus.Succeeded, JobStatus.Succeeded, JobStatus.Succeeded, RunStatus.Succeeded)]
    [InlineData(JobStatus.Succeeded, JobStatus.Skipped, JobStatus.Succeeded, RunStatus.Succeeded)]
    [InlineData(JobStatus.Failed, JobStatus.Skipped, JobStatus.Succeeded, RunStatus.Failed)]
    [InlineData(JobStatus.Succeeded, JobStatus.Succeeded, JobStatus.Failed, RunStatus.Failed)]
    [InlineData(JobStatus.Failed, JobStatus.Cancelled, JobStatus.Failed, RunStatus.Cancelled)]
    public void The_run_status_follows_its_jobs(JobStatus check, JobStatus fix, JobStatus report, RunStatus expected) =>
        RunOutcome.Of(Load(Automation), State(("check", check), ("fix", fix), ("report", report))).ShouldBe(expected);

    [Fact]
    public void A_cancel_request_decides_the_main_outcome()
    {
        var state = State(("check", JobStatus.Succeeded), ("fix", JobStatus.Running)) with { CancelRequested = true };

        RunOutcome.OfMainJobs(Load(Automation), state).ShouldBe(RunStatus.Cancelled);
    }

    [Fact]
    public void Main_jobs_outcome_ignores_finally_jobs() =>
        RunOutcome.OfMainJobs(Load(Automation), State(("check", JobStatus.Succeeded), ("fix", JobStatus.Succeeded))).ShouldBe(RunStatus.Succeeded);

    [Theory]
    [InlineData(RunStatus.Running, "running")]
    [InlineData(RunStatus.Succeeded, "succeeded")]
    [InlineData(RunStatus.Failed, "failed")]
    [InlineData(RunStatus.Cancelled, "cancelled")]
    public void Statuses_have_words_for_expressions(RunStatus status, string word) =>
        RunOutcome.Describe(status).ShouldBe(word);

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void Changed_when_decides_whether_a_run_changed_something(string? changed, bool expected) =>
        RunOutcome.Changed(Load(Automation), new DictionaryResolver(new Dictionary<string, string?>(StringComparer.Ordinal) { ["jobs.check.outputs.changed"] = changed }))
            .ShouldBe(expected);

    [Fact]
    public void Without_changed_when_every_run_is_a_change() =>
        RunOutcome.Changed(Load("version: 1\njobs:\n  a: { steps: [ { run: a } ] }\n"), new DictionaryResolver([])).ShouldBeTrue();

    [Fact]
    public void An_unparsable_changed_when_counts_as_a_change()
    {
        var automation = Load("version: 1\njobs:\n  a: { steps: [ { run: a } ] }\n") with { ChangedWhen = "a ==" };

        RunOutcome.Changed(automation, new DictionaryResolver([])).ShouldBeTrue();
    }

    [Fact]
    public void Arguments_must_not_be_null()
    {
        Should.Throw<ArgumentNullException>(() => RunOutcome.OfMainJobs(null!, State()));
        Should.Throw<ArgumentNullException>(() => RunOutcome.OfMainJobs(Load(Automation), null!));
        Should.Throw<ArgumentNullException>(() => RunOutcome.Changed(null!, new DictionaryResolver([])));
        Should.Throw<ArgumentNullException>(() => RunOutcome.Changed(Load(Automation), null!));
    }
}
