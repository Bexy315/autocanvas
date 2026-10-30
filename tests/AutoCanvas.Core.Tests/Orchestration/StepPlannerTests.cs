using AutoCanvas.Core.Model;
using AutoCanvas.Core.Orchestration;
using AutoCanvas.Core.Tests.Expressions;

namespace AutoCanvas.Core.Tests.Orchestration;

public sealed class StepPlannerTests
{
    private static readonly StepJob Job = (StepJob)RunPlannerTests.Load("""
        version: 1
        jobs:
          build:
            steps:
              - { id: compile, run: make }
              - { name: Lint, run: lint, continue_on_error: true }
              - { if: steps.compile.outputs.publish == 'yes', run: publish }
              - run: summary
        """).Jobs[0];

    private static readonly DictionaryResolver Publish = new(new Dictionary<string, string?>(StringComparer.Ordinal) { ["steps.compile.outputs.publish"] = "yes" });
    private static readonly DictionaryResolver NoPublish = new(new Dictionary<string, string?>(StringComparer.Ordinal));

    [Fact]
    public void Steps_run_in_order_and_the_job_succeeds()
    {
        StepPlanner.Next(Job, [], Publish).ShouldBe(new RunStep(0));
        StepPlanner.Next(Job, [StepOutcome.Succeeded], Publish).ShouldBe(new RunStep(1));
        StepPlanner.Next(Job, [StepOutcome.Succeeded, StepOutcome.Succeeded], Publish).ShouldBe(new RunStep(2));
        StepPlanner.Next(Job, [StepOutcome.Succeeded, StepOutcome.Succeeded, StepOutcome.Succeeded, StepOutcome.Succeeded], Publish)
            .ShouldBe(new FinishJob(JobStatus.Succeeded));
    }

    [Fact]
    public void A_false_condition_skips_only_that_step()
    {
        StepPlanner.Next(Job, [StepOutcome.Succeeded, StepOutcome.Succeeded], NoPublish)
            .ShouldBe(new SkipStep(2, "Its condition is false: steps.compile.outputs.publish == 'yes'"));
        StepPlanner.Next(Job, [StepOutcome.Succeeded, StepOutcome.Succeeded, StepOutcome.Skipped], NoPublish).ShouldBe(new RunStep(3));
    }

    [Fact]
    public void After_a_failure_the_remaining_steps_are_skipped_and_the_job_fails()
    {
        StepPlanner.Next(Job, [StepOutcome.Failed], Publish).ShouldBe(new SkipStep(1, "Step 1 (compile) failed."));
        StepPlanner.Next(Job, [StepOutcome.Failed, StepOutcome.Skipped, StepOutcome.Skipped, StepOutcome.Skipped], Publish)
            .ShouldBe(new FinishJob(JobStatus.Failed));
    }

    [Theory]
    [InlineData(StepOutcome.Failed)]
    [InlineData(StepOutcome.TimedOut)]
    public void Continue_on_error_forgives_failures_of_that_step(StepOutcome outcome)
    {
        StepPlanner.Next(Job, [StepOutcome.Succeeded, outcome], Publish).ShouldBe(new RunStep(2));
        StepPlanner.JobResult(Job, [StepOutcome.Succeeded, outcome, StepOutcome.Succeeded, StepOutcome.Succeeded]).ShouldBe(JobStatus.Succeeded);
    }

    [Fact]
    public void Continue_on_error_does_not_forgive_runner_errors() =>
        StepPlanner.Next(Job, [StepOutcome.Succeeded, StepOutcome.Errored], Publish).ShouldBe(new SkipStep(2, "Step 2 (Lint) failed."));

    [Fact]
    public void A_cancelled_step_cancels_the_job()
    {
        StepPlanner.Next(Job, [StepOutcome.Cancelled], Publish).ShouldBe(new SkipStep(1, "The job was cancelled."));
        StepPlanner.JobResult(Job, [StepOutcome.Failed, StepOutcome.Cancelled]).ShouldBe(JobStatus.Cancelled);
    }

    [Fact]
    public void Unnamed_steps_are_described_as_such()
    {
        var job = (StepJob)RunPlannerTests.Load("version: 1\njobs:\n  a: { steps: [ { run: a }, { run: b } ] }\n").Jobs[0];

        StepPlanner.Next(job, [StepOutcome.Failed], Publish).ShouldBe(new SkipStep(1, "Step 1 (unnamed) failed."));
    }

    [Fact]
    public void Arguments_must_not_be_null()
    {
        Should.Throw<ArgumentNullException>(() => StepPlanner.Next(null!, [], Publish));
        Should.Throw<ArgumentNullException>(() => StepPlanner.Next(Job, null!, Publish));
        Should.Throw<ArgumentNullException>(() => StepPlanner.Next(Job, [], null!));
        Should.Throw<ArgumentNullException>(() => StepPlanner.JobResult(null!, []));
        Should.Throw<ArgumentNullException>(() => StepPlanner.JobResult(Job, null!));
    }
}
