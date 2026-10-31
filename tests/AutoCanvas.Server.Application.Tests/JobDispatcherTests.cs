using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Sqlite;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Tests;

public abstract class JobDispatcherTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private static readonly HashSet<string> None = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Linux = new(StringComparer.Ordinal) { "linux" };
    private static readonly HashSet<string> Http = new(StringComparer.Ordinal) { "http_request" };

    // The SQLite claimer's semaphore holds no handle, so the test does not dispose it.
    private readonly IJobClaimer claimer = provider == DatabaseProvider.Postgres ? new PostgresJobClaimer() : new SqliteJobClaimer();
    private readonly Guid runnerA = Guid.CreateVersion7();
    private readonly Guid runnerB = Guid.CreateVersion7();

    [Fact]
    public async Task Runners_get_only_jobs_whose_labels_node_types_and_executor_they_have()
    {
        await StartAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              plain:
                runs_on: linux
                steps:
                  - run: echo plain
              node:
                runs_on: linux
                steps:
                  - type: http_request
                    with:
                      url: https://example.org
              boxed:
                image: alpine:3.20
                steps:
                  - run: echo boxed
            """);

        (await ClaimAsync(runnerA, None, None, containers: false)).ShouldBeNull();
        var plain = await ClaimAsync(runnerA, Linux, None, containers: false);
        (await ClaimAsync(runnerA, Linux, None, containers: false)).ShouldBeNull();
        var rest = new[] { await ClaimAsync(runnerB, Linux, Http, containers: true), await ClaimAsync(runnerB, Linux, Http, containers: true) };

        (await JobOfAsync(plain!.Value)).ShouldBe("plain");
        (await Task.WhenAll(rest.Select(a => JobOfAsync(a!.Value)))).ShouldBe(["node", "boxed"], ignoreOrder: true);
        (await ClaimAsync(runnerB, Linux, Http, containers: true)).ShouldBeNull();
        var offered = await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == plain.Value, Cancel));
        offered.Status.ShouldBe(AttemptStatus.Offered);
        offered.RunnerId.ShouldBe(runnerA);
        offered.LeaseExpiresAt.ShouldBe(Clock.GetUtcNow() + TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Concurrent_claims_hand_out_every_attempt_exactly_once()
    {
        const int Jobs = 24;
        await StartAsync("version: 1\ntriggers:\n  manual: true\njobs:\n"
            + string.Concat(Enumerable.Range(0, Jobs).Select(i => $"  job{i}:\n    steps:\n      - run: echo {i}\n")));

        var claimed = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            var mine = new List<Guid>();
            var runner = Guid.CreateVersion7();
            while (await ClaimAsync(runner, None, None, containers: false) is { } id)
            {
                mine.Add(id);
            }

            return mine;
        })));

        var all = claimed.SelectMany(ids => ids).ToList();
        all.Count.ShouldBe(Jobs);
        all.ShouldBeUnique();
    }

    [Fact]
    public async Task Runners_accept_reject_and_renew_only_their_own_attempts()
    {
        await StartAsync(TwoJobs.Replace("    needs: [build]\n", string.Empty, StringComparison.Ordinal));
        var first = (await ClaimAsync(runnerA, None, None, containers: false))!.Value;
        var second = (await ClaimAsync(runnerA, None, None, containers: false))!.Value;

        (await DispatchAsync(d => d.AcceptAsync(first, runnerB, Cancel))).ShouldBeFalse();
        (await DispatchAsync(d => d.AcceptAsync(first, runnerA, Cancel))).ShouldBeTrue();
        (await DispatchAsync(d => d.RejectAsync(first, runnerA, Cancel))).ShouldBeFalse();
        (await DispatchAsync(d => d.RejectAsync(second, runnerA, Cancel))).ShouldBeTrue();
        Clock.Advance(TimeSpan.FromSeconds(30));
        var unknown = Guid.CreateVersion7();
        (await DispatchAsync(d => d.RenewAsync(runnerA, [first, second, unknown], Cancel))).ShouldBe([second, unknown], ignoreOrder: true);

        var accepted = await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == first, Cancel));
        accepted.Status.ShouldBe(AttemptStatus.Running);
        accepted.LeaseExpiresAt.ShouldBe(Clock.GetUtcNow() + TimeSpan.FromMinutes(1));
        (await ClaimAsync(runnerB, None, None, containers: false)).ShouldBe(second);
    }

    [Fact]
    public async Task Finishing_a_job_starts_the_jobs_that_need_it_and_closes_the_run()
    {
        var run = await StartAsync(TwoJobs);
        var build = (await ClaimAsync(runnerA, None, None, containers: false))!.Value;
        await DispatchAsync(d => d.AcceptAsync(build, runnerA, Cancel));

        (await DispatchAsync(d => d.FinishAsync(build, runnerB, AttemptOutcome.Succeeded, null, Cancel))).ShouldBeFalse();
        (await DispatchAsync(d => d.FinishAsync(build, runnerA, AttemptOutcome.Succeeded, null, Cancel))).ShouldBeTrue();
        var test = (await ClaimAsync(runnerA, None, None, containers: false))!.Value;
        (await JobOfAsync(test)).ShouldBe("test");
        await DispatchAsync(d => d.AcceptAsync(test, runnerA, Cancel));
        await DispatchAsync(d => d.FinishAsync(test, runnerA, AttemptOutcome.Succeeded, null, Cancel));

        var done = await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == run, Cancel));
        done.State.ShouldBe(RunState.Succeeded);
        done.FinishedAt.ShouldBe(Clock.GetUtcNow());
        (await DispatchAsync(d => d.FinishAsync(test, runnerA, AttemptOutcome.Succeeded, null, Cancel))).ShouldBeFalse();
    }

    [Fact]
    public async Task Lost_runners_lose_their_jobs_and_unaccepted_offers_go_back()
    {
        var run = await StartAsync(TwoJobs.Replace("version: 1", "version: 1\nname: lost", StringComparison.Ordinal));
        var build = (await ClaimAsync(runnerA, None, None, containers: false))!.Value;
        await DispatchAsync(d => d.AcceptAsync(build, runnerA, Cancel));
        await StartAsync(TwoJobs, "other");
        var offered = (await ClaimAsync(runnerB, None, None, containers: false))!.Value;

        Clock.Advance(TimeSpan.FromSeconds(61));
        (await DispatchAsync(d => d.SweepAsync(Cancel))).ShouldBe(2);

        var lost = await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == build, Cancel));
        lost.Status.ShouldBe(AttemptStatus.Failed);
        lost.Error!.ShouldContain("stopped responding");
        (await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == offered, Cancel))).Status.ShouldBe(AttemptStatus.Queued);
        var failed = await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == run, Cancel));
        failed.State.ShouldBe(RunState.Failed);
        (await ReadAsync(db => db.RunJobs.SingleAsync(j => j.RunId == run && j.Key == "test", Cancel))).Status.ShouldBe(RunJobStatus.Skipped);
        (await DispatchAsync(d => d.SweepAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Failed_jobs_with_retry_are_queued_again_after_the_delay()
    {
        var run = await StartAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              flaky:
                retry:
                  max_attempts: 2
                  delay_seconds: 30
                steps:
                  - run: exit 1
            """);
        var first = (await ClaimAsync(runnerA, None, None, containers: false))!.Value;
        await DispatchAsync(d => d.AcceptAsync(first, runnerA, Cancel));

        await DispatchAsync(d => d.FinishAsync(first, runnerA, AttemptOutcome.Failed, null, Cancel));
        var early = await ClaimAsync(runnerA, None, None, containers: false);
        Clock.Advance(TimeSpan.FromSeconds(30));
        var second = (await ClaimAsync(runnerA, None, None, containers: false))!.Value;
        await DispatchAsync(d => d.AcceptAsync(second, runnerA, Cancel));
        await DispatchAsync(d => d.FinishAsync(second, runnerA, AttemptOutcome.Failed, null, Cancel));

        early.ShouldBeNull();
        var job = await ReadAsync(db => db.RunJobs.SingleAsync(j => j.RunId == run, Cancel));
        job.AttemptCount.ShouldBe(2);
        job.Status.ShouldBe(RunJobStatus.Failed);
        (await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == run, Cancel))).State.ShouldBe(RunState.Failed);
    }

    [Fact]
    public async Task Jobs_that_run_past_their_time_limit_fail()
    {
        var run = await StartAsync("""
            version: 1
            triggers:
              manual: true
            timeout_minutes: 5
            jobs:
              slow:
                timeout_minutes: 2
                steps:
                  - run: sleep 1000
              default:
                steps:
                  - run: sleep 1
            """);
        // Both were queued at the same instant, so which comes first is open; tell them apart by job.
        var claimed = new[]
        {
            (await ClaimAsync(runnerA, None, None, containers: false, lease: TimeSpan.FromHours(1)))!.Value,
            (await ClaimAsync(runnerA, None, None, containers: false, lease: TimeSpan.FromHours(1)))!.Value,
        };
        var slow = string.Equals(await JobOfAsync(claimed[0]), "slow", StringComparison.Ordinal) ? claimed[0] : claimed[1];
        var other = claimed.Single(id => id != slow);
        await DispatchAsync(d => d.AcceptAsync(slow, runnerA, Cancel), TimeSpan.FromHours(1));
        await DispatchAsync(d => d.AcceptAsync(other, runnerA, Cancel), TimeSpan.FromHours(1));

        Clock.Advance(TimeSpan.FromMinutes(3));
        (await DispatchAsync(d => d.SweepAsync(Cancel), TimeSpan.FromHours(1))).ShouldBe(1);

        var timedOut = await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == slow, Cancel));
        timedOut.Error!.ShouldContain("time limit of 2 minutes");
        (await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == other, Cancel))).TimeoutMinutes.ShouldBe(5);
        (await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == run, Cancel))).State.ShouldBe(RunState.Running);
    }

    [Fact]
    public async Task An_attempt_of_a_cancelled_run_only_catches_up()
    {
        var run = await StartAsync(TwoJobs);
        var build = (await ClaimAsync(runnerA, None, None, containers: false))!.Value;
        await DispatchAsync(d => d.AcceptAsync(build, runnerA, Cancel));
        var ada = await ActorAsync("ada");
        await RunsAsync(r => r.CancelAsync(ada, run, Cancel));

        (await DispatchAsync(d => d.FinishAsync(build, runnerA, AttemptOutcome.Cancelled, null, Cancel))).ShouldBeTrue();

        (await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == build, Cancel))).Status.ShouldBe(AttemptStatus.Cancelled);
        (await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == run, Cancel))).State.ShouldBe(RunState.Cancelled);
    }

    private const string TwoJobs = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            steps:
              - run: echo build
          test:
            needs: [build]
            steps:
              - run: echo test
        """;

    private async Task<Guid> StartAsync(string yaml, string name = "flow")
    {
        // The end of a version 7 UUID is random; its start is the time and repeats within a millisecond.
        var unique = Guid.CreateVersion7().ToString("N")[^8..];
        var ada = await ActorAsync("ada" + unique);
        var folder = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder(name + unique), Cancel))).ShouldSucceed().Id;
        var automation = (await AutomationsAsync(a => a.CreateAsync(ada, folder, name, yaml, null, Cancel))).ShouldSucceed().Id;
        return (await RunsAsync(r => r.StartAsync(ada, automation, Cancel))).ShouldSucceed().Id;
    }

    private Task<Guid?> ClaimAsync(Guid runner, IReadOnlySet<string> labels, IReadOnlySet<string> nodeTypes, bool containers, TimeSpan? lease = null) =>
        DispatchAsync(d => d.ClaimAsync(runner, labels, nodeTypes, containers, Cancel), lease);

    private async Task<T> DispatchAsync<T>(Func<JobDispatcher, Task<T>> action, TimeSpan? lease = null)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            var options = Options.Create(new QueueOptions { LeaseDuration = lease ?? TimeSpan.FromMinutes(1) });
            return await action(new JobDispatcher(context, claimer, new ConfigurationService(context, new SecretCipher(Key)), options, Clock));
        }
    }

    private Task<string> JobOfAsync(Guid attempt) =>
        ReadAsync(db => db.JobAttempts.Where(a => a.Id == attempt).Join(db.RunJobs, a => a.RunJobId, j => j.Id, (_, j) => j.Key).SingleAsync(Cancel));
}

public sealed class JobDispatcherTestsOnSqlite() : JobDispatcherTests(DatabaseProvider.Sqlite);

public sealed class JobDispatcherTestsOnPostgres() : JobDispatcherTests(DatabaseProvider.Postgres);
