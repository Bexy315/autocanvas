using System.Text.Json;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Sqlite;
using AutoCanvas.Persistence.Triggers;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Tests;

public abstract class TriggerTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private static readonly HashSet<string> Linux = new(StringComparer.Ordinal) { "linux" };

    private readonly IJobClaimer claimer = provider == DatabaseProvider.Postgres ? new PostgresJobClaimer() : new SqliteJobClaimer();
    private Actor owner = null!;
    private Guid folder;

    [Fact]
    public async Task Saving_an_automation_keeps_its_schedule_entries_with_their_next_times()
    {
        await SetUpAsync();
        var id = await CreateAsync("nightly", Triggers("schedule:\n    - cron: '0 3 * * *'\n      timezone: Europe/Berlin\n    - interval: 10m"));

        var schedules = await SchedulesAsync(id);
        schedules.Select(s => (s.Position, s.Cron, s.TimeZone, s.IntervalSeconds)).ShouldBe([(0, "0 3 * * *", "Europe/Berlin", (long?)null), (1, null, null, 600)]);
        schedules[0].NextAt.ShouldBe(new DateTimeOffset(2026, 10, 31, 2, 0, 0, TimeSpan.Zero));
        schedules[1].NextAt.ShouldBe(Clock.GetUtcNow().AddMinutes(10));

        Clock.Advance(TimeSpan.FromMinutes(3));
        await SaveAsync(id, Triggers("schedule:\n    - interval: 10m\n    - interval: 1h"));
        var saved = await SchedulesAsync(id);

        saved.Select(s => (s.Position, s.IntervalSeconds)).ShouldBe([(0, (long?)600), (1, 3600)]);
        saved[0].NextAt.ShouldBe(schedules[1].NextAt);
        saved[1].NextAt.ShouldBe(Clock.GetUtcNow().AddHours(1));
        var view = (await TriggersAsync(owner, id)).ShouldSucceed();
        view.Schedules.Select(s => s.Schedule).ShouldBe(["every 10m", "every 1h"]);
        view.Manual.ShouldBeFalse();
        view.Webhook.ShouldBeFalse();
        view.Follows.ShouldBeNull();

        await SaveAsync(id, "version: 1\ntriggers:\n  manual: true\njobs:\n  build:\n    runs_on: linux\n    steps:\n      - run: echo hi\n");
        (await SchedulesAsync(id)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("schedule:\n    - cron: '61 * * * *'", "triggers.schedule[0].cron", "not a valid cron expression")]
    [InlineData("schedule:\n    - cron: '0 0 30 2 *'", "triggers.schedule[0].cron", "never fires")]
    [InlineData("schedule:\n    - cron: '0 3 * * *'\n      timezone: Mars/Olympus", "triggers.schedule[0].timezone", "not a known time zone")]
    [InlineData("automation_completed:\n    automation: missing", "triggers.automation_completed.automation", "no automation 'missing'")]
    [InlineData("automation_completed:\n    automation: nowhere/build", "triggers.automation_completed.automation", "no folder 'nowhere'")]
    public async Task Triggers_that_cannot_work_are_refused_with_a_diagnostic(string triggers, string path, string message)
    {
        await SetUpAsync();

        var error = (await AutomationsAsync(a => a.CreateAsync(owner, folder, "broken", Triggers(triggers), null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);

        var problem = error.Diagnostics.ShouldNotBeNull().ShouldHaveSingleItem();
        problem.Code.ShouldBe(DiagnosticCodes.InvalidValue);
        problem.Path.ShouldBe(path);
        problem.Message.ShouldContain(message);
    }

    [Fact]
    public async Task An_automation_follows_another_by_name_or_path_that_its_author_may_see()
    {
        await SetUpAsync();
        var build = await CreateAsync("build", Triggers("manual: true"));
        var other = (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder("Other"), Cancel))).ShouldSucceed().Id;
        var deploy = await CreateAsync("deploy", Triggers("automation_completed:\n    automation: build"));
        var remote = (await AutomationsAsync(a => a.CreateAsync(owner, other, "remote", Triggers($"automation_completed:\n    automation: {FolderName}/build\n    on: always"), null, Cancel))).ShouldSucceed().Id;
        var stranger = await ActorAsync("stranger" + Guid.NewGuid().ToString("N")[..6]);
        var theirs = (await FoldersAsync(f => f.CreateAsync(stranger, new NewFolder("Theirs" + Guid.NewGuid().ToString("N")[..6]), Cancel))).ShouldSucceed().Id;

        var hidden = (await AutomationsAsync(a => a.CreateAsync(stranger, theirs, "spy", Triggers($"automation_completed:\n    automation: {FolderName}/build"), null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        var self = (await AutomationsAsync(a => a.SaveAsync(owner, build, Triggers("automation_completed:\n    automation: build"), null, null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);

        (await TriggersAsync(owner, deploy)).ShouldSucceed().Follows.ShouldBe(new CompletionView(folder, "build", CompletionOutcome.Success));
        (await ReadAsync(db => db.CompletionTriggers.SingleAsync(c => c.AutomationId == remote, Cancel))).On.ShouldBe(CompletionOutcome.Always);
        hidden.Diagnostics!.ShouldHaveSingleItem().Message.ShouldContain("no automation");
        self.Diagnostics!.ShouldHaveSingleItem().Message.ShouldContain("cannot start when it finishes itself");
        (await TriggersAsync(stranger, deploy)).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task A_due_schedule_starts_one_run_and_missed_times_are_not_made_up()
    {
        await SetUpAsync();
        var id = await CreateAsync("every", Triggers("schedule:\n    - interval: 10m"));
        Clock.Advance(TimeSpan.FromMinutes(5));
        (await DispatchAsync(d => d.StartScheduledAsync(Cancel))).Started.ShouldBeEmpty();

        Clock.Advance(TimeSpan.FromHours(1));
        var round = await DispatchAsync(d => d.StartScheduledAsync(Cancel));
        var again = await DispatchAsync(d => d.StartScheduledAsync(Cancel));

        round.Started.ShouldBe(["'every' #1"]);
        again.Started.ShouldBeEmpty();
        var run = await ReadAsync(db => db.Runs.SingleAsync(r => r.AutomationId == id, Cancel));
        run.Trigger.ShouldBe(RunTriggerKind.Schedule);
        run.TriggeredByName.ShouldBe("schedule");
        JsonDocument.Parse(run.TriggerData!).RootElement.GetProperty("schedule").GetString().ShouldBe("every 10m");
        var schedule = (await SchedulesAsync(id)).ShouldHaveSingleItem();
        schedule.NextAt.ShouldBe(Clock.GetUtcNow().AddMinutes(10));
        schedule.LastAt.ShouldBe(Clock.GetUtcNow());
        (await AuditAsync(AuditActions.RunStarted)).ShouldHaveSingleItem().Details!.ShouldContain("schedule");
    }

    [Fact]
    public async Task A_single_automation_still_running_skips_its_schedule()
    {
        await SetUpAsync();
        var id = await CreateAsync("single", Triggers("manual: true\n  schedule:\n    - interval: 1m") + "concurrency: single\n");
        (await RunsAsync(r => r.StartAsync(owner, id, Cancel))).ShouldSucceed();
        Clock.Advance(TimeSpan.FromMinutes(2));

        var round = await DispatchAsync(d => d.StartScheduledAsync(Cancel));

        round.Started.ShouldBeEmpty();
        round.Skipped.ShouldHaveSingleItem().ShouldContain("one run at a time");
        (await SchedulesAsync(id)).ShouldHaveSingleItem().NextAt.ShouldBe(Clock.GetUtcNow().AddMinutes(1));
    }

    [Fact]
    public async Task Finished_runs_start_the_automations_that_follow_their_result()
    {
        await SetUpAsync();
        var build = await CreateAsync("build", Triggers("manual: true"));
        var onSuccess = await CreateAsync("deploy", Triggers("automation_completed:\n    automation: build"));
        var onFailure = await CreateAsync("alert", Triggers("automation_completed:\n    automation: build\n    on: failure"));
        var always = await CreateAsync("report", Triggers("automation_completed:\n    automation: build\n    on: always"));

        var run = (await RunsAsync(r => r.StartAsync(owner, build, Cancel))).ShouldSucceed();
        await SucceedAsync();
        var round = await DispatchAsync(d => d.StartFollowersAsync(Cancel));
        var again = await DispatchAsync(d => d.StartFollowersAsync(Cancel));

        round.Started.Order(StringComparer.Ordinal).ShouldBe(["'deploy' #1", "'report' #1"]);
        again.Started.ShouldBeEmpty();
        var started = await ReadAsync(db => db.Runs.SingleAsync(r => r.AutomationId == onSuccess, Cancel));
        started.Trigger.ShouldBe(RunTriggerKind.AutomationCompleted);
        started.TriggeredByName.ShouldBe("build #1");
        var data = JsonDocument.Parse(started.TriggerData!).RootElement;
        data.GetProperty("run_id").GetString().ShouldBe(run.Id.ToString());
        data.GetProperty("result").GetString().ShouldBe("success");
        data.GetProperty("depth").GetString().ShouldBe("1");
        (await ReadAsync(db => db.Runs.AnyAsync(r => r.AutomationId == onFailure, Cancel))).ShouldBeFalse();
        (await ReadAsync(db => db.Runs.AnyAsync(r => r.AutomationId == always, Cancel))).ShouldBeTrue();
    }

    [Fact]
    public async Task A_chain_of_automations_stops_after_ten()
    {
        await SetUpAsync();
        var build = await CreateAsync("build", Triggers("manual: true"));
        await CreateAsync("next", Triggers("automation_completed:\n    automation: build\n    on: always"));
        var run = (await RunsAsync(r => r.StartAsync(owner, build, Cancel))).ShouldSucceed();
        (await RunsAsync(r => r.CancelAsync(owner, run.Id, Cancel))).ShouldSucceed();
        await ReadAsync(db => db.Runs.Where(r => r.Id == run.Id).ExecuteUpdateAsync(
            set => set.SetProperty(r => r.Trigger, RunTriggerKind.AutomationCompleted).SetProperty(r => r.TriggerData, """{"depth":"10"}"""),
            Cancel));

        var round = await DispatchAsync(d => d.StartFollowersAsync(Cancel));

        round.Started.ShouldBeEmpty();
        round.Skipped.ShouldHaveSingleItem().ShouldContain("chain of 10");
    }

    [Fact]
    public async Task Moving_an_automation_resolves_a_name_alone_in_its_new_folder()
    {
        await SetUpAsync();
        await CreateAsync("build", Triggers("manual: true"));
        var deploy = await CreateAsync("deploy", Triggers("automation_completed:\n    automation: build"));
        var other = (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder("Elsewhere"), Cancel))).ShouldSucceed().Id;

        var moved = (await AutomationsAsync(a => a.MoveAsync(owner, deploy, other, null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);

        moved.Diagnostics!.ShouldHaveSingleItem().Path.ShouldBe("triggers.automation_completed.automation");
    }

    private string FolderName { get; set; } = string.Empty;

    private static string Triggers(string triggers) => $"""
        version: 1
        triggers:
          {triggers}
        jobs:
          build:
            runs_on: linux
            steps:
              - run: echo hi

        """;

    private async Task SetUpAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        owner = await ActorAsync("owner" + unique);
        FolderName = "team" + unique;
        folder = (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder(FolderName), Cancel))).ShouldSucceed().Id;
    }

    private async Task<Guid> CreateAsync(string name, string yaml) =>
        (await AutomationsAsync(a => a.CreateAsync(owner, folder, name, yaml, null, Cancel))).ShouldSucceed().Id;

    private async Task SaveAsync(Guid id, string yaml) =>
        (await AutomationsAsync(a => a.SaveAsync(owner, id, yaml, null, null, Cancel))).ShouldSucceed();

    private Task<List<AutomationSchedule>> SchedulesAsync(Guid id) =>
        ReadAsync(db => db.AutomationSchedules.Where(s => s.AutomationId == id).OrderBy(s => s.Position).ToListAsync(Cancel));

    private async Task<UseCaseResult<AutomationTriggersView>> TriggersAsync(Actor actor, Guid id)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await new AutomationTriggerService(context).GetAsync(actor, id, Cancel);
        }
    }

    private async Task<T> DispatchAsync<T>(Func<TriggerDispatcher, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new TriggerDispatcher(context, Signal, new ConfigurationService(context, new SecretCipher(Key)), new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }

    /// <summary>Lets a runner take the queued job and finish it successfully.</summary>
    private async Task SucceedAsync()
    {
        var runner = Guid.CreateVersion7();
        var context = Database.CreateContext();
        await using (context)
        {
            var dispatcher = new JobDispatcher(context, claimer, Signal, new ConfigurationService(context, new SecretCipher(Key)), Options.Create(new QueueOptions()), Clock);
            var attempt = (await dispatcher.ClaimAsync(runner, Linux, Linux, runsContainers: true, Cancel)).ShouldNotBeNull();
            (await dispatcher.AcceptAsync(attempt, runner, Cancel)).ShouldBeTrue();
            (await dispatcher.FinishAsync(attempt, runner, AttemptOutcome.Succeeded, null, Cancel)).ShouldBeTrue();
        }
    }
}

public sealed class TriggerTestsOnSqlite() : TriggerTests(DatabaseProvider.Sqlite);

public sealed class TriggerTestsOnPostgres() : TriggerTests(DatabaseProvider.Postgres);
