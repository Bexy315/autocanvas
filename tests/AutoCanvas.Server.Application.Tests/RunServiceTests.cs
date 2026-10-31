using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class RunServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Flow = """
        version: 1
        name: Flow
        triggers:
          manual: true
        jobs:
          build:
            steps:
              - run: echo build
          nightly:
            if: trigger.event == 'schedule'
            steps:
              - run: echo never by hand
          route:
            type: switch
            expression: ${{ vars.TARGET }}
            cases:
              - name: prod
                when: prod
              - name: other
                default: true
          deploy:
            needs: [{ job: route, case: prod }]
            steps:
              - run: echo deploy
          done:
            type: join
            needs: [deploy]
          cleanup:
            finally: true
            on_cancel: true
            steps:
              - run: echo cleanup
        """;

    private const string Single = """
        version: 1
        triggers:
          manual: true
        concurrency: single
        jobs:
          only:
            steps:
              - run: echo one at a time
        """;

    private const string NotByHand = """
        version: 1
        triggers:
          schedule:
            - interval: 5m
        jobs:
          only:
            steps:
              - run: echo scheduled
        """;

    [Fact]
    public async Task Starting_a_run_plans_what_can_happen_now_and_queues_the_rest()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(team), "TARGET", "prod", Cancel));
        var flow = await AutomationAsync(ada, team, "flow", Flow);

        var run = (await RunsAsync(r => r.StartAsync(ada, flow, Cancel))).ShouldSucceed();

        run.Number.ShouldBe(1);
        run.State.ShouldBe(RunState.Running);
        run.TriggeredByName.ShouldBe("ada");
        run.Jobs.Select(j => j.Key).ShouldBe(["build", "nightly", "route", "deploy", "done", "cleanup"]);
        Job(run, "build").Status.ShouldBe(RunJobStatus.Running);
        Job(run, "build").Attempts.ShouldBe(1);
        Job(run, "nightly").Status.ShouldBe(RunJobStatus.Skipped);
        Job(run, "nightly").Reason.ShouldNotBeNullOrEmpty();
        Job(run, "route").Status.ShouldBe(RunJobStatus.Succeeded);
        Job(run, "route").ActivatedCase.ShouldBe("prod");
        Job(run, "deploy").Status.ShouldBe(RunJobStatus.Running);
        Job(run, "done").Status.ShouldBe(RunJobStatus.Pending);
        Job(run, "cleanup").Status.ShouldBe(RunJobStatus.Pending);
        (await ReadAsync(db => db.JobAttempts.CountAsync(a => a.Status == AttemptStatus.Queued, Cancel))).ShouldBe(2);
        (await AuditAsync(AuditActions.RunStarted)).ShouldHaveSingleItem().FolderId.ShouldBe(team);
        (await RunsAsync(r => r.StartAsync(ada, flow, Cancel))).ShouldSucceed().Number.ShouldBe(2);
    }

    [Fact]
    public async Task Without_a_matching_case_the_branch_is_skipped()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var flow = await AutomationAsync(ada, team, "flow", Flow);

        var run = (await RunsAsync(r => r.StartAsync(ada, flow, Cancel))).ShouldSucceed();

        Job(run, "route").ActivatedCase.ShouldBe("other");
        Job(run, "deploy").Status.ShouldBe(RunJobStatus.Skipped);
        Job(run, "done").Status.ShouldBe(RunJobStatus.Skipped);
    }

    [Fact]
    public async Task Cancelling_stops_the_jobs_and_lets_finally_jobs_run()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var flow = await AutomationAsync(ada, team, "flow", Flow);
        var run = (await RunsAsync(r => r.StartAsync(ada, flow, Cancel))).ShouldSucceed();

        var cancelled = (await RunsAsync(r => r.CancelAsync(ada, run.Id, Cancel))).ShouldSucceed();
        var again = (await RunsAsync(r => r.CancelAsync(ada, run.Id, Cancel))).ShouldSucceed();

        cancelled.CancelRequested.ShouldBeTrue();
        cancelled.CancelledByName.ShouldBe("ada");
        Job(cancelled, "build").Status.ShouldBe(RunJobStatus.Cancelled);
        Job(cancelled, "cleanup").Status.ShouldBe(RunJobStatus.Running);
        cancelled.State.ShouldBe(RunState.Running);
        again.ShouldBeEquivalentTo(cancelled);
        (await ReadAsync(db => db.JobAttempts.Where(a => a.Status == AttemptStatus.Cancelled).CountAsync(Cancel))).ShouldBe(1);
        (await AuditAsync(AuditActions.RunCancelled)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_run_where_nothing_is_left_to_do_finishes_at_once()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var skipped = await AutomationAsync(ada, team, "skipped", """
            version: 1
            triggers:
              manual: true
            jobs:
              nightly:
                if: trigger.event == 'schedule'
                steps:
                  - run: echo never
            """);

        var run = (await RunsAsync(r => r.StartAsync(ada, skipped, Cancel))).ShouldSucceed();

        run.State.ShouldBe(RunState.Succeeded);
        run.FinishedAt.ShouldBe(Clock.GetUtcNow());
        (await RunsAsync(r => r.CancelAsync(ada, run.Id, Cancel))).ShouldSucceed().CancelRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_automations_that_allow_it_start_by_hand_and_single_ones_once_at_a_time()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var scheduled = await AutomationAsync(ada, team, "scheduled", NotByHand);
        var single = await AutomationAsync(ada, team, "single", Single);

        (await RunsAsync(r => r.StartAsync(ada, scheduled, Cancel))).ShouldFail(UseCaseErrorKind.Validation).Message.ShouldContain("manual: true");
        var first = (await RunsAsync(r => r.StartAsync(ada, single, Cancel))).ShouldSucceed();
        (await RunsAsync(r => r.StartAsync(ada, single, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        await RunsAsync(r => r.CancelAsync(ada, first.Id, Cancel));
        (await RunsAsync(r => r.StartAsync(ada, single, Cancel))).ShouldSucceed().Number.ShouldBe(2);
    }

    [Fact]
    public async Task Operators_start_and_cancel_viewers_read_and_others_see_nothing()
    {
        var ada = await ActorAsync("ada");
        var vic = await ActorAsync("vic");
        var olga = await ActorAsync("olga");
        var eve = await ActorAsync("eve");
        var team = await FolderAsync(ada, "Team");
        await GrantAsync(ada, team, vic, FolderRole.Viewer);
        await GrantAsync(ada, team, olga, FolderRole.Operator);
        var flow = await AutomationAsync(ada, team, "flow", Flow);

        var run = (await RunsAsync(r => r.StartAsync(olga, flow, Cancel))).ShouldSucceed();

        (await RunsAsync(r => r.GetAsync(vic, run.Id, Cancel))).ShouldSucceed();
        (await RunsAsync(r => r.ListAsync(vic, flow, new PageRequest(), Cancel))).ShouldSucceed().Items.ShouldHaveSingleItem();
        (await RunsAsync(r => r.StartAsync(vic, flow, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await RunsAsync(r => r.CancelAsync(vic, run.Id, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await RunsAsync(r => r.GetAsync(eve, run.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await RunsAsync(r => r.StartAsync(eve, flow, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await RunsAsync(r => r.ListAsync(eve, flow, new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await RunsAsync(r => r.GetAsync(ada, Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await RunsAsync(r => r.StartAsync(ada, Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await RunsAsync(r => r.CancelAsync(olga, run.Id, Cancel))).ShouldSucceed();
    }

    [Fact]
    public async Task Runs_outlive_their_automation_and_go_with_their_folder()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var flow = await AutomationAsync(ada, team, "flow", Flow);
        var run = (await RunsAsync(r => r.StartAsync(ada, flow, Cancel))).ShouldSucceed();

        (await AutomationsAsync(a => a.DeleteAsync(ada, flow, null, Cancel))).ShouldSucceed();
        var orphan = (await RunsAsync(r => r.GetAsync(ada, run.Id, Cancel))).ShouldSucceed();
        (await FoldersAsync(f => f.DeleteAsync(ada, team, null, Cancel))).ShouldSucceed();

        orphan.AutomationId.ShouldBeNull();
        orphan.AutomationName.ShouldBe("flow");
        (await ReadAsync(db => db.Runs.CountAsync(Cancel))).ShouldBe(0);
        (await ReadAsync(db => db.JobAttempts.CountAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Runs_are_listed_newest_first_one_page_at_a_time()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var flow = await AutomationAsync(ada, team, "flow", Flow);
        for (var i = 0; i < 3; i++)
        {
            await RunsAsync(r => r.StartAsync(ada, flow, Cancel));
        }

        var first = (await RunsAsync(r => r.ListAsync(ada, flow, new PageRequest(2), Cancel))).ShouldSucceed();
        var second = (await RunsAsync(r => r.ListAsync(ada, flow, new PageRequest(2, first.Next), Cancel))).ShouldSucceed();

        first.Items.Select(r => r.Number).ShouldBe([3, 2]);
        second.Items.Select(r => r.Number).ShouldBe([1]);
        second.Next.ShouldBeNull();
        (await RunsAsync(r => r.ListAsync(ada, flow, new PageRequest(2, "x"), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await RunsAsync(r => r.ListAsync(ada, flow, new PageRequest(0), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await RunsAsync(r => r.ListAsync(ada, Guid.CreateVersion7(), new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    private static Runs.RunJobView Job(Runs.RunView run, string key) => run.Jobs.Single(j => string.Equals(j.Key, key, StringComparison.Ordinal));

    private async Task<Guid> FolderAsync(Actor owner, string name) =>
        (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder(name), Cancel))).ShouldSucceed().Id;

    private async Task<Guid> AutomationAsync(Actor owner, Guid folderId, string name, string yaml) =>
        (await AutomationsAsync(a => a.CreateAsync(owner, folderId, name, yaml, null, Cancel))).ShouldSucceed().Id;

    private async Task GrantAsync(Actor owner, Guid folderId, Actor user, FolderRole role) =>
        (await FolderGrantsAsync(g => g.SetAsync(owner, folderId, user.UserId, null, role, Cancel))).ShouldSucceed();
}

public sealed class RunServiceTestsOnSqlite() : RunServiceTests(DatabaseProvider.Sqlite);

public sealed class RunServiceTestsOnPostgres() : RunServiceTests(DatabaseProvider.Postgres);
