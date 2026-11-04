using System.Text.Json;
using AutoCanvas.Core.Orchestration;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Sqlite;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RunState = AutoCanvas.Persistence.Runs.RunState;

namespace AutoCanvas.Server.Application.Tests;

public abstract class JobProgressTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private static readonly HashSet<string> Linux = new(StringComparer.Ordinal) { "linux" };
    private static readonly HashSet<string> Http = new(StringComparer.Ordinal) { "http_request" };
    private static readonly Dictionary<string, string> NoOutputs = new(StringComparer.Ordinal);

    private readonly IJobClaimer claimer = provider == DatabaseProvider.Postgres ? new PostgresJobClaimer() : new SqliteJobClaimer();
    private readonly Guid runner = Guid.CreateVersion7();

    [Fact]
    public async Task Steps_are_sent_one_at_a_time_with_the_outputs_of_the_steps_before_them()
    {
        var run = await StartAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                steps:
                  - id: version
                    name: Find the version
                    run: echo tag=v1 >> $AUTOCANVAS_OUTPUT
                    timeout_minutes: 3
                  - run: echo building ${{ steps.version.outputs.tag }}
                    shell: sh
                    working_directory: src
                outputs:
                  tag: ${{ steps.version.outputs.tag }}
              deploy:
                needs: [build]
                runs_on: linux
                env:
                  TAG: ${{ jobs.build.outputs.tag }}
                steps:
                  - run: deploy $TAG
            """);
        var attempt = await TakeAsync();

        var first = Step(await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel)));
        await ProgressAsync(p => p.StepStartedAsync(attempt, runner, 0, Clock.GetUtcNow(), Cancel));
        var second = Step(await CompleteAsync(attempt, 0, StepOutcome.Succeeded, ("tag", "v1")));
        var done = await CompleteAsync(attempt, 1, StepOutcome.Succeeded);
        var finished = await ProgressAsync(p => p.FinishedAsync(attempt, runner, [], Cancel));

        first.Index.ShouldBe(0);
        first.StepId.ShouldBe("version");
        first.Name.ShouldBe("Find the version");
        first.Timeout.ShouldBe(TimeSpan.FromMinutes(3));
        first.Script!.Shell.ShouldBe("bash");
        // The first step names no shell: bash is the server's choice, which the runner may replace with sh.
        first.Script.DefaultShell.ShouldBeTrue();
        first.MaskValues.ShouldBeEmpty();
        second.Script.ShouldBe(new ResolvedScript("sh", "echo building v1", "src"));
        second.Name.ShouldBe("Step 2");
        done.ShouldBeOfType<CompleteJobInstruction>();
        finished.ShouldBeTrue();
        var steps = await ReadAsync(db => db.JobSteps.Where(s => s.AttemptId == attempt).OrderBy(s => s.Index).ToListAsync(Cancel));
        steps.Select(s => s.Status).ShouldBe([JobStepStatus.Succeeded, JobStepStatus.Succeeded]);
        steps[0].StartedAt.ShouldNotBeNull();
        steps[0].Outputs.ShouldBe("""{"tag":"v1"}""");
        var jobs = await ReadAsync(db => db.RunJobs.Where(j => j.RunId == run).OrderBy(j => j.Position).ToListAsync(Cancel));
        jobs[0].Status.ShouldBe(RunJobStatus.Succeeded);
        jobs[0].Outputs.ShouldBe("""{"tag":"v1"}""");
        jobs[1].Status.ShouldBe(RunJobStatus.Running);
        (await OfferAsync(await TakeAsync())).Env["TAG"].ShouldBe("v1");
    }

    [Fact]
    public async Task Steps_whose_condition_is_false_are_skipped_and_never_sent()
    {
        await StartAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                steps:
                  - if: ${{ vars.DEPLOY == 'yes' }}
                    run: deploy
                  - run: echo always
            """);
        var attempt = await TakeAsync();

        var sent = Step(await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel)));

        sent.Index.ShouldBe(1);
        var skipped = await ReadAsync(db => db.JobSteps.SingleAsync(s => s.AttemptId == attempt && s.Index == 0, Cancel));
        skipped.Status.ShouldBe(JobStepStatus.Skipped);
        skipped.Reason!.ShouldContain("condition is false");
    }

    [Theory]
    [InlineData(false, RunJobStatus.Failed)]
    [InlineData(true, RunJobStatus.Succeeded)]
    public async Task A_failed_step_skips_the_rest_and_fails_the_job_unless_it_may_fail(bool continueOnError, RunJobStatus expected)
    {
        var run = await StartAsync($"""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                steps:
                  - run: exit 3
                    continue_on_error: {(continueOnError ? "true" : "false")}
                  - run: echo after
            """);
        var attempt = await TakeAsync();
        await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel));

        var next = await ProgressAsync(p => p.StepCompletedAsync(attempt, runner, new StepReport(0, StepOutcome.Failed, 3, NoOutputs, "exit code 3", null), Cancel));
        if (continueOnError)
        {
            Step(next).Index.ShouldBe(1);
            await CompleteAsync(attempt, 1, StepOutcome.Succeeded);
        }
        else
        {
            next.ShouldBeOfType<CompleteJobInstruction>();
        }

        await ProgressAsync(p => p.FinishedAsync(attempt, runner, [], Cancel));

        var job = await ReadAsync(db => db.RunJobs.SingleAsync(j => j.RunId == run, Cancel));
        job.Status.ShouldBe(expected);
        var steps = await ReadAsync(db => db.JobSteps.Where(s => s.AttemptId == attempt).OrderBy(s => s.Index).ToListAsync(Cancel));
        steps[0].ExitCode.ShouldBe(3);
        steps[0].Error.ShouldBe("exit code 3");
        steps[1].Status.ShouldBe(continueOnError ? JobStepStatus.Succeeded : JobStepStatus.Skipped);
        var finished = await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == attempt, Cancel));
        finished.Error.ShouldBe(continueOnError ? null : "exit code 3");
    }

    [Fact]
    public async Task Secrets_travel_only_with_the_steps_that_use_them_and_come_back_masked()
    {
        await StartAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                env:
                  AUTH: Bearer ${{ secrets.API_TOKEN }}
                  PLAIN: plain
                steps:
                  - id: login
                    run: login
                    env:
                      PASSWORD: ${{ secrets.PASSWORD }}
                  - run: echo ${{ steps.login.outputs.echo }}
                outputs:
                  echo: ${{ steps.login.outputs.echo }}
            """, ("API_TOKEN", "token-value-1"), ("PASSWORD", "password-value-2"));
        var attempt = await TakeAsync();

        var offer = await OfferAsync(attempt);
        var first = Step(await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel)));
        var second = Step(await CompleteAsync(attempt, 0, StepOutcome.Succeeded, ("echo", "it was password-value-2")));

        offer.Env.Keys.ShouldBe(["PLAIN"]);
        first.Env.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal) { ["AUTH"] = "Bearer token-value-1", ["PASSWORD"] = "password-value-2" }, ignoreOrder: true);
        first.MaskValues.ShouldBe(["token-value-1", "password-value-2"], ignoreOrder: true);
        second.Script!.Text.ShouldBe("echo it was ***");
        second.Env.Keys.ShouldBe(["AUTH"]);
        second.MaskValues.ShouldBe(["token-value-1"]);
        (await ReadAsync(db => db.JobSteps.SingleAsync(s => s.AttemptId == attempt && s.Index == 0, Cancel))).Outputs.ShouldBe("""{"echo":"it was ***"}""");
    }

    [Fact]
    public async Task Node_steps_get_their_inputs_rendered_and_add_outputs_of_their_own()
    {
        await StartAsync("""
            version: 1
            triggers:
              manual: true
            variables:
              HOST: example.org
            jobs:
              call:
                runs_on: linux
                steps:
                  - id: ping
                    type: http_request
                    with:
                      url: https://${{ vars.HOST }}/ping
                      timeout_seconds: 3
                      headers:
                        Accept: text/plain
                    outputs:
                      ok: ${{ steps.ping.outputs.status == '200' }}
                outputs:
                  ok: ${{ steps.ping.outputs.ok }}
            """);
        var attempt = await TakeAsync(Http);

        var node = Step(await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel))).Node!;
        await CompleteAsync(attempt, 0, StepOutcome.Succeeded, ("status", "200"));

        node.Type.ShouldBe("http_request");
        JsonDocument.Parse(node.Inputs).RootElement.GetRawText().ShouldBe("""{"url":"https://example.org/ping","timeout_seconds":3,"headers":{"Accept":"text/plain"}}""");
        (await ReadAsync(db => db.JobSteps.SingleAsync(s => s.AttemptId == attempt, Cancel))).Outputs.ShouldBe("""{"status":"200","ok":"true"}""");
        (await ReadAsync(db => db.RunJobs.SingleAsync(j => j.Key == "call", Cancel))).Outputs.ShouldBe("""{"ok":"true"}""");
    }

    [Fact]
    public async Task A_failed_preparation_completes_the_job_and_fails_it()
    {
        var run = await StartAsync(OneStep);
        var attempt = await TakeAsync();

        (await ProgressAsync(p => p.PreparedAsync(attempt, runner, "image not found", Cancel))).ShouldBeOfType<CompleteJobInstruction>();
        (await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel))).ShouldBeOfType<CompleteJobInstruction>();
        (await ProgressAsync(p => p.PendingAsync(attempt, runner, Cancel))).ShouldBeOfType<CompleteJobInstruction>();
        await ProgressAsync(p => p.FinishedAsync(attempt, runner, ["no reports found"], Cancel));

        var finished = await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == attempt, Cancel));
        finished.Status.ShouldBe(AttemptStatus.Failed);
        finished.Error.ShouldBe("image not found");
        finished.Warnings.ShouldBe("""["no reports found"]""");
        (await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == run, Cancel))).State.ShouldBe(RunState.Failed);
    }

    [Fact]
    public async Task Cancelling_a_run_stops_its_running_attempts_through_their_runner()
    {
        var run = await StartAsync(TwoSteps);
        var attempt = await TakeAsync();
        await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel));
        var ada = await ActorAsync("cancel-ada", isAdmin: true);

        await RunsAsync(r => r.CancelAsync(ada, run, Cancel));
        var stopping = await ProgressAsync(p => p.CancellationsAsync(runner, Cancel));
        var next = await ProgressAsync(p => p.StepCompletedAsync(attempt, runner, new StepReport(0, StepOutcome.Cancelled, null, NoOutputs, null, null), Cancel));
        await ProgressAsync(p => p.FinishedAsync(attempt, runner, [], Cancel));

        stopping.ShouldHaveSingleItem().ShouldBe((attempt, "The run was cancelled."));
        next.ShouldBeNull();
        (await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == attempt, Cancel))).Status.ShouldBe(AttemptStatus.Cancelled);
        (await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == run, Cancel))).State.ShouldBe(RunState.Cancelled);
        (await ProgressAsync(p => p.CancellationsAsync(runner, Cancel))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_a_run_withdraws_offers_nobody_accepted_yet()
    {
        var run = await StartAsync(OneStep);
        var offered = (await DispatchAsync(d => d.ClaimAsync(runner, Linux, Linux, runsContainers: false, Cancel)))!.Value;

        await RunsAsync(async r => await r.CancelAsync(await ActorAsync("ada-withdraw", isAdmin: true), run, Cancel));

        (await DispatchAsync(d => d.AcceptAsync(offered, runner, Cancel))).ShouldBeFalse();
        (await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == offered, Cancel))).Status.ShouldBe(AttemptStatus.Cancelled);
    }

    [Fact]
    public async Task A_job_that_ran_out_of_time_is_stopped_and_fails()
    {
        await StartAsync("version: 1\ntriggers:\n  manual: true\njobs:\n  slow:\n    runs_on: linux\n    timeout_minutes: 1\n    steps:\n      - run: sleep 600\n");
        var attempt = await TakeAsync();
        await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel));
        Clock.Advance(TimeSpan.FromSeconds(70));
        await DispatchAsync(d => d.RenewAsync(runner, [attempt], Cancel));

        await DispatchAsync(d => d.SweepAsync(Cancel));
        var stopping = await ProgressAsync(p => p.CancellationsAsync(runner, Cancel));
        await ProgressAsync(p => p.FinishedAsync(attempt, runner, [], Cancel));

        stopping.ShouldHaveSingleItem().Reason.ShouldContain("time limit of 1 minutes");
        var finished = await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == attempt, Cancel));
        finished.Status.ShouldBe(AttemptStatus.Failed);
        finished.Error!.ShouldContain("time limit");
        (await ReadAsync(db => db.JobSteps.SingleAsync(s => s.AttemptId == attempt, Cancel))).Status.ShouldBe(JobStepStatus.Cancelled);
    }

    [Fact]
    public async Task After_a_reconnect_the_sent_step_is_sent_again_and_repeated_reports_are_ignored()
    {
        await StartAsync(TwoSteps);
        var attempt = await TakeAsync();
        (await ProgressAsync(p => p.PendingAsync(attempt, runner, Cancel))).ShouldBeNull();
        await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel));

        var resent = Step(await ProgressAsync(p => p.PendingAsync(attempt, runner, Cancel)));
        await ProgressAsync(p => p.StepStartedAsync(attempt, runner, 0, null, Cancel));
        var whileRunning = await ProgressAsync(p => p.PendingAsync(attempt, runner, Cancel));
        var next = Step(await CompleteAsync(attempt, 0, StepOutcome.Succeeded));
        var repeated = await CompleteAsync(attempt, 0, StepOutcome.Failed);
        var again = Step(await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel)));

        resent.Index.ShouldBe(0);
        whileRunning.ShouldBeNull();
        next.Index.ShouldBe(1);
        repeated.ShouldBeNull();
        again.Index.ShouldBe(1);
        (await ReadAsync(db => db.JobSteps.SingleAsync(s => s.AttemptId == attempt && s.Index == 0, Cancel))).Status.ShouldBe(JobStepStatus.Succeeded);
    }

    [Fact]
    public async Task Only_the_runner_that_holds_a_running_attempt_moves_it_on()
    {
        await StartAsync(TwoSteps);
        var attempt = await TakeAsync();
        var other = Guid.CreateVersion7();

        (await ProgressAsync(p => p.PreparedAsync(attempt, other, null, Cancel))).ShouldBeNull();
        (await ProgressAsync(p => p.StepCompletedAsync(attempt, other, new StepReport(0, StepOutcome.Succeeded, 0, NoOutputs, null, null), Cancel))).ShouldBeNull();
        (await ProgressAsync(p => p.FinishedAsync(attempt, other, [], Cancel))).ShouldBeFalse();
        (await ProgressAsync(p => p.PreparedAsync(Guid.CreateVersion7(), runner, null, Cancel))).ShouldBeNull();
        (await ReadAsync(db => db.JobSteps.CountAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task A_runner_that_finishes_before_all_steps_ran_fails_the_job()
    {
        await StartAsync(TwoSteps);
        var attempt = await TakeAsync();
        await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel));

        await ProgressAsync(p => p.FinishedAsync(attempt, runner, [], Cancel));

        var finished = await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == attempt, Cancel));
        finished.Status.ShouldBe(AttemptStatus.Failed);
        finished.Error!.ShouldContain("before all its steps ran");
        (await ReadAsync(db => db.JobSteps.SingleAsync(s => s.AttemptId == attempt, Cancel))).Status.ShouldBe(JobStepStatus.Errored);
    }

    [Fact]
    public async Task Outputs_beyond_the_limit_fail_the_step()
    {
        await StartAsync(TwoSteps);
        var attempt = await TakeAsync();
        await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel));

        var next = await CompleteAsync(attempt, 0, StepOutcome.Succeeded, ("huge", new string('x', JobStep.MaxOutputsLength)));

        next.ShouldBeOfType<CompleteJobInstruction>();
        var step = await ReadAsync(db => db.JobSteps.SingleAsync(s => s.AttemptId == attempt && s.Index == 0, Cancel));
        step.Status.ShouldBe(JobStepStatus.Errored);
        step.Outputs.ShouldBeNull();
        step.Error!.ShouldContain("64 KiB");
    }

    private const string OneStep = "version: 1\ntriggers:\n  manual: true\njobs:\n  build:\n    runs_on: linux\n    steps:\n      - run: echo one\n";

    private const string TwoSteps = "version: 1\ntriggers:\n  manual: true\njobs:\n  build:\n    runs_on: linux\n    steps:\n      - run: echo one\n      - run: echo two\n";

    private static ResolvedStep Step(JobInstruction? instruction) => instruction.ShouldBeOfType<RunStepInstruction>().Step;

    private Task<JobInstruction?> CompleteAsync(Guid attempt, int index, StepOutcome outcome, params (string Name, string Value)[] outputs) =>
        ProgressAsync(p => p.StepCompletedAsync(
            attempt,
            runner,
            new StepReport(index, outcome, 0, outputs.ToDictionary(o => o.Name, o => o.Value, StringComparer.Ordinal), null, Clock.GetUtcNow()),
            Cancel));

    private async Task<Guid> StartAsync(string yaml, params (string Name, string Value)[] secrets)
    {
        var unique = Guid.CreateVersion7().ToString("N")[^8..];
        var ada = await ActorAsync("ada" + unique, isAdmin: true);
        var folder = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("team" + unique), Cancel))).ShouldSucceed().Id;
        foreach (var (name, value) in secrets)
        {
            (await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Folder(folder), name, value, Cancel))).ShouldSucceed();
        }

        var automation = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "flow", yaml, null, Cancel))).ShouldSucceed().Id;
        return (await RunsAsync(r => r.StartAsync(ada, automation, Cancel))).ShouldSucceed().Id;
    }

    /// <summary>Claims and accepts the next attempt for the test's runner.</summary>
    private async Task<Guid> TakeAsync(IReadOnlySet<string>? nodeTypes = null)
    {
        var attempt = (await DispatchAsync(d => d.ClaimAsync(runner, Linux, nodeTypes ?? new HashSet<string>(StringComparer.Ordinal), runsContainers: true, Cancel))).ShouldNotBeNull();
        (await DispatchAsync(d => d.AcceptAsync(attempt, runner, Cancel))).ShouldBeTrue();
        return attempt;
    }

    private async Task<OfferedJob> OfferAsync(Guid attempt)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return (await new JobOfferBuilder(context, new ConfigurationService(context, new SecretCipher(Key)), new SecretCipher(Key), Clock).BuildAsync(attempt, Cancel)).ShouldSucceed();
        }
    }

    private async Task<T> DispatchAsync<T>(Func<JobDispatcher, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(Dispatcher(context));
        }
    }

    private async Task<T> ProgressAsync<T>(Func<JobProgress, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            var configuration = new ConfigurationService(context, new SecretCipher(Key));
            return await action(new JobProgress(context, configuration, Dispatcher(context), Clock));
        }
    }

    private async Task ProgressAsync(Func<JobProgress, Task> action) => await ProgressAsync(async p =>
    {
        await action(p);
        return true;
    });

    private JobDispatcher Dispatcher(AutoCanvasDbContext context) =>
        new(context, claimer, Signal, new ConfigurationService(context, new SecretCipher(Key)), Options.Create(new QueueOptions()), Clock);
}

public sealed class JobProgressTestsOnSqlite() : JobProgressTests(DatabaseProvider.Sqlite);

public sealed class JobProgressTestsOnPostgres() : JobProgressTests(DatabaseProvider.Postgres);
