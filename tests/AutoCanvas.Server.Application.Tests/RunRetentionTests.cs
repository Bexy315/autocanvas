using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Reports;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Tests;

public abstract class RunRetentionTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Yaml = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            runs_on: linux
            steps:
              - run: echo hi
        """;

    [Fact]
    public async Task Old_finished_runs_go_with_their_logs_and_reports_and_the_newest_stay()
    {
        var (owner, automation) = await SetUpAsync();
        var runs = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            runs.Add(await FinishedRunAsync(owner, automation, daysAgo: 100));
        }

        var recent = await FinishedRunAsync(owner, automation, daysAgo: 10);
        var running = (await RunsAsync(r => r.StartAsync(owner, automation, Cancel))).ShouldSucceed().Id;
        await AgeAsync(running, 200, finished: false);
        await AddLogAndReportAsync(runs[0]);

        var deleted = await PruneAsync(new RunOptions { RetentionDays = 90, KeepPerAutomation = 3 });

        // Kept: the three newest by number (run 4 of the old ones, the recent one, the running one).
        deleted.ShouldBe(3);
        var left = await ReadAsync(db => db.Runs.Select(r => r.Id).ToListAsync(Cancel));
        left.ShouldBe([runs[3], recent, running], ignoreOrder: true);
        (await ReadAsync(db => db.LogChunks.CountAsync(Cancel))).ShouldBe(0);
        (await ReadAsync(db => db.Reports.CountAsync(Cancel))).ShouldBe(0);
        (await ReadAsync(db => db.RunJobs.CountAsync(j => runs.Take(3).Contains(j.RunId), Cancel))).ShouldBe(0);
        (await AuditAsync(AuditActions.RunsPruned)).ShouldHaveSingleItem().Details!.ShouldContain("\"deleted\":3");
    }

    [Fact]
    public async Task Runs_of_deleted_automations_count_by_age_alone_and_zero_keeps_everything()
    {
        var (owner, automation) = await SetUpAsync();
        var old = await FinishedRunAsync(owner, automation, daysAgo: 100);
        var young = await FinishedRunAsync(owner, automation, daysAgo: 5);
        (await AutomationsAsync(a => a.DeleteAsync(owner, automation, null, Cancel))).ShouldSucceed();

        (await PruneAsync(new RunOptions { RetentionDays = 0 })).ShouldBe(0);
        (await PruneAsync(new RunOptions { RetentionDays = 90, KeepPerAutomation = 20 })).ShouldBe(1);
        (await PruneAsync(new RunOptions { RetentionDays = 90, KeepPerAutomation = 0 })).ShouldBe(0);

        (await ReadAsync(db => db.Runs.Select(r => r.Id).ToListAsync(Cancel))).ShouldBe([young]);
        (await ReadAsync(db => db.Runs.AnyAsync(r => r.Id == old, Cancel))).ShouldBeFalse();
    }

    [Fact]
    public async Task Keeping_none_of_an_automation_deletes_all_its_old_runs()
    {
        var (owner, automation) = await SetUpAsync();
        await FinishedRunAsync(owner, automation, daysAgo: 100);
        await FinishedRunAsync(owner, automation, daysAgo: 100);

        (await PruneAsync(new RunOptions { RetentionDays = 30, KeepPerAutomation = 0 })).ShouldBe(2);
    }

    private async Task<(Actor Owner, Guid Automation)> SetUpAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var owner = await ActorAsync("owner" + unique);
        var folder = (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder("runs" + unique), Cancel))).ShouldSucceed().Id;
        var automation = (await AutomationsAsync(a => a.CreateAsync(owner, folder, "build", Yaml, null, Cancel))).ShouldSucceed().Id;
        return (owner, automation);
    }

    private async Task<Guid> FinishedRunAsync(Actor owner, Guid automation, int daysAgo)
    {
        var run = (await RunsAsync(r => r.StartAsync(owner, automation, Cancel))).ShouldSucceed().Id;
        (await RunsAsync(r => r.CancelAsync(owner, run, Cancel))).ShouldSucceed();
        await AgeAsync(run, daysAgo, finished: true);
        return run;
    }

    private Task<int> AgeAsync(Guid run, int daysAgo, bool finished) =>
        ReadAsync(db => db.Runs.Where(r => r.Id == run).ExecuteUpdateAsync(
            set => set.SetProperty(r => r.CreatedAt, Clock.GetUtcNow().AddDays(-daysAgo))
                .SetProperty(r => r.FinishedAt, finished ? Clock.GetUtcNow().AddDays(-daysAgo) : null),
            Cancel));

    private Task<int> AddLogAndReportAsync(Guid run) => ReadAsync(async db =>
    {
        var attempt = await db.JobAttempts.Where(a => db.RunJobs.Any(j => j.Id == a.RunJobId && j.RunId == run)).Select(a => a.Id).FirstAsync(Cancel);
        db.LogChunks.Add(new LogChunk { AttemptId = attempt, Sequence = 0, Lines = "[]", LineCount = 0, ByteCount = 0, ReceivedAt = Clock.GetUtcNow() });
        db.Reports.Add(new Report { Id = Guid.CreateVersion7(), AttemptId = attempt, RunId = run, JobKey = "build", Name = "junit", Format = "junit", Kind = ReportKind.Tests, Path = "a.xml", Size = 1, ReceivedAt = Clock.GetUtcNow() });
        return await db.SaveChangesAsync(Cancel);
    });

    private async Task<int> PruneAsync(RunOptions options)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await new RunRetention(context, new AuditLog(context, new SystemAuditContext(), Clock), Options.Create(options), Clock).PruneAsync(Cancel);
        }
    }
}

public sealed class RunRetentionTestsOnSqlite() : RunRetentionTests(DatabaseProvider.Sqlite);

public sealed class RunRetentionTestsOnPostgres() : RunRetentionTests(DatabaseProvider.Postgres);
