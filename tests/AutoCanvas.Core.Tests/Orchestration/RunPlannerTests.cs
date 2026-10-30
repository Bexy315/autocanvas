using AutoCanvas.Core.Model;
using AutoCanvas.Core.Orchestration;
using AutoCanvas.Core.Tests.Expressions;

namespace AutoCanvas.Core.Tests.Orchestration;

public sealed class RunPlannerTests
{
    private static readonly DictionaryResolver NoValues = new(new Dictionary<string, string?>(StringComparer.Ordinal));

    internal static AutomationDefinition Load(string yaml)
    {
        var result = AutomationLoader.Load(yaml);
        result.Diagnostics.ShouldBeEmpty(string.Join(Environment.NewLine, result.Diagnostics));
        return result.Definition!;
    }

    internal static RunState State(params (string Job, JobStatus Status)[] jobs) =>
        new(jobs.ToDictionary(j => j.Job, j => new JobState(j.Status), StringComparer.Ordinal));

    private static IReadOnlyList<JobAction> Plan(string yaml, RunState? state = null, DictionaryResolver? values = null) =>
        RunPlanner.Plan(Load(yaml), state ?? State(), values ?? NoValues);

    private const string Chain = """
        version: 1
        jobs:
          build: { steps: [ { run: a } ] }
          test: { needs: [build], steps: [ { run: a } ] }
          notify: { needs: [{ job: test, on: failure }], steps: [ { run: a } ] }
          always: { needs: [{ job: test, on: always }], steps: [ { run: a } ] }
        """;

    [Fact]
    public void Jobs_without_needs_start_first() =>
        Plan(Chain).ShouldBe([new StartJob("build")]);

    [Fact]
    public void Nothing_happens_while_a_need_is_running() =>
        Plan(Chain, State(("build", JobStatus.Running))).ShouldBeEmpty();

    [Fact]
    public void A_succeeded_need_starts_its_dependents() =>
        Plan(Chain, State(("build", JobStatus.Succeeded))).ShouldBe([new StartJob("test")]);

    [Fact]
    public void Failure_and_always_needs_react_to_a_failure() =>
        Plan(Chain, State(("build", JobStatus.Succeeded), ("test", JobStatus.Failed)))
            .ShouldBe([new StartJob("notify"), new StartJob("always")]);

    [Fact]
    public void A_failure_need_is_skipped_on_success()
    {
        var actions = Plan(Chain, State(("build", JobStatus.Succeeded), ("test", JobStatus.Succeeded)));

        actions.ShouldBe([new SkipJob("notify", "'test' succeeded, but this job needs it to have failed."), new StartJob("always")]);
    }

    [Fact]
    public void Skips_propagate_within_one_call()
    {
        var actions = Plan(Chain, State(("build", JobStatus.Failed)));

        actions.Select(a => (a.GetType().Name, a.JobId)).ShouldBe([
            ("SkipJob", "test"),
            ("SkipJob", "notify"),
            ("StartJob", "always"),
        ]);
        actions[0].ShouldBeOfType<SkipJob>().Reason.ShouldBe("'build' failed, but this job needs it to have succeeded.");
    }

    [Fact]
    public void A_false_condition_skips_the_job()
    {
        const string yaml = """
            version: 1
            jobs:
              deploy: { if: trigger.branch == 'main', steps: [ { run: a } ] }
            """;

        Plan(yaml, values: new(new Dictionary<string, string?>(StringComparer.Ordinal) { ["trigger.branch"] = "dev" }))
            .ShouldBe([new SkipJob("deploy", "Its condition is false: trigger.branch == 'main'")]);
        Plan(yaml, values: new(new Dictionary<string, string?>(StringComparer.Ordinal) { ["trigger.branch"] = "main" }))
            .ShouldBe([new StartJob("deploy")]);
    }

    [Fact]
    public void Conditions_see_results_decided_in_the_same_call()
    {
        var actions = Plan("""
            version: 1
            jobs:
              a: { steps: [ { run: a } ] }
              b: { needs: [a], steps: [ { run: a } ] }
              c: { needs: [{ job: b, on: always }], if: "jobs.b.result == 'skipped'", steps: [ { run: a } ] }
            """, State(("a", JobStatus.Failed)));

        actions.ShouldBe([new SkipJob("b", "'a' failed, but this job needs it to have succeeded."), new StartJob("c")]);
    }

    [Theory]
    [InlineData(JobStatus.Succeeded, "success")]
    [InlineData(JobStatus.Failed, "failure")]
    [InlineData(JobStatus.Cancelled, "cancelled")]
    [InlineData(JobStatus.Skipped, "skipped")]
    public void Job_results_have_words_for_conditions(JobStatus status, string word)
    {
        var yaml = $$"""
            version: 1
            jobs:
              a: { steps: [ { run: a } ] }
              b: { needs: [{ job: a, on: always }], if: "jobs.a.result == '{{word}}'", steps: [ { run: a } ] }
            """;

        Plan(yaml, State(("a", status))).ShouldBe([new StartJob("b")]);
    }

    [Theory]
    [InlineData("main", "prod")]
    [InlineData("dev", "other")]
    [InlineData("MAIN", "other")]
    public void Switches_activate_the_first_matching_case_or_the_default(string branch, string activated)
    {
        var actions = Plan(SwitchAutomation, values: Branch(branch));

        actions[0].ShouldBe(new CompleteSwitch("route", branch, activated));
    }

    [Fact]
    public void Jobs_on_other_cases_are_skipped_and_joins_pass()
    {
        var actions = Plan(SwitchAutomation, values: Branch("main"));

        actions.Skip(1).ShouldBe([
            new StartJob("deploy"),
            new SkipJob("fallback", "'route' activated case 'prod', but this job runs on case 'other'."),
        ]);

        var later = RunPlanner.Plan(Load(SwitchAutomation), State(("route", JobStatus.Succeeded), ("deploy", JobStatus.Succeeded), ("fallback", JobStatus.Skipped)), NoValues);
        later.ShouldBe([new CompleteJoin("done")]);
    }

    [Fact]
    public void A_switch_without_match_or_default_activates_nothing()
    {
        var actions = Plan("""
            version: 1
            jobs:
              route: { type: switch, expression: '${{ trigger.branch }}', cases: [ { name: prod, when: main } ] }
              deploy: { needs: [{ job: route, case: prod }], steps: [ { run: a } ] }
            """, values: Branch("dev"));

        actions.ShouldBe([
            new CompleteSwitch("route", "dev", null),
            new SkipJob("deploy", "'route' activated no case, but this job runs on case 'prod'."),
        ]);
    }

    [Fact]
    public void A_case_of_a_failed_switch_is_skipped()
    {
        var state = new RunState(new Dictionary<string, JobState>(StringComparer.Ordinal) { ["route"] = new(JobStatus.Cancelled) });

        RunPlanner.Plan(Load(SwitchAutomation), state, NoValues)
            .OfType<SkipJob>().First().Reason.ShouldBe("'route' was cancelled, but this job runs on case 'prod'.");
    }

    [Fact]
    public void Finally_jobs_wait_for_all_others_then_run()
    {
        const string yaml = """
            version: 1
            jobs:
              build: { steps: [ { run: a } ] }
              report: { finally: true, steps: [ { run: a } ] }
            """;

        Plan(yaml).ShouldBe([new StartJob("build")]);
        Plan(yaml, State(("build", JobStatus.Running))).ShouldBeEmpty();
        Plan(yaml, State(("build", JobStatus.Failed))).ShouldBe([new StartJob("report")]);
    }

    [Fact]
    public void Cancelling_stops_everything_but_waiting_on_cancel_finally_jobs()
    {
        var automation = Load(CancelAutomation);
        var state = State(("build", JobStatus.Running), ("done", JobStatus.Succeeded));

        RunPlanner.Cancel(automation, state).ShouldBe([new CancelJob("build"), new CancelJob("test"), new CancelJob("report")]);
    }

    [Fact]
    public void After_a_cancel_only_on_cancel_finally_jobs_run()
    {
        var automation = Load(CancelAutomation);
        var stopped = State(("build", JobStatus.Cancelled), ("test", JobStatus.Pending), ("done", JobStatus.Succeeded)) with { CancelRequested = true };

        RunPlanner.Plan(automation, stopped, NoValues).ShouldBe([
            new CancelJob("test"),
            new SkipJob("report", "The run was cancelled; this finally job does not run on cancel."),
            new StartJob("cleanup"),
        ]);
    }

    [Fact]
    public void Retries_follow_the_policy()
    {
        var job = (StepJob)Load("version: 1\njobs:\n  a: { retry: { max_attempts: 3, delay_seconds: 7 }, steps: [ { run: a } ] }\n").Jobs[0];
        var plain = (StepJob)Load("version: 1\njobs:\n  a: { steps: [ { run: a } ] }\n").Jobs[0];

        RunPlanner.RetryDelay(job, 1).ShouldBe(TimeSpan.FromSeconds(7));
        RunPlanner.RetryDelay(job, 2).ShouldBe(TimeSpan.FromSeconds(7));
        RunPlanner.RetryDelay(job, 3).ShouldBeNull();
        RunPlanner.RetryDelay(plain, 1).ShouldBeNull();
    }

    [Fact]
    public void Arguments_must_not_be_null()
    {
        var automation = Load(Chain);
        Should.Throw<ArgumentNullException>(() => RunPlanner.Plan(null!, State(), NoValues));
        Should.Throw<ArgumentNullException>(() => RunPlanner.Plan(automation, null!, NoValues));
        Should.Throw<ArgumentNullException>(() => RunPlanner.Plan(automation, State(), null!));
        Should.Throw<ArgumentNullException>(() => RunPlanner.Cancel(null!, State()));
        Should.Throw<ArgumentNullException>(() => RunPlanner.Cancel(automation, null!));
        Should.Throw<ArgumentNullException>(() => RunPlanner.RetryDelay(null!, 1));
    }

    private static DictionaryResolver Branch(string branch) =>
        new(new Dictionary<string, string?>(StringComparer.Ordinal) { ["trigger.branch"] = branch });

    private const string SwitchAutomation = """
        version: 1
        jobs:
          route:
            type: switch
            expression: ${{ trigger.branch }}
            cases:
              - { name: prod, when: main }
              - { name: other, default: true }
          deploy: { needs: [{ job: route, case: prod }], steps: [ { run: a } ] }
          fallback: { needs: [{ job: route, case: other }], steps: [ { run: a } ] }
          done: { type: join, needs: [{ job: deploy, on: always }, { job: fallback, on: always }] }
        """;

    private const string CancelAutomation = """
        version: 1
        jobs:
          done: { steps: [ { run: a } ] }
          build: { steps: [ { run: a } ] }
          test: { needs: [build], steps: [ { run: a } ] }
          report: { finally: true, steps: [ { run: a } ] }
          cleanup: { finally: true, on_cancel: true, steps: [ { run: a } ] }
        """;
}
