using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Sqlite;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Persistence.Tests;

public abstract class JobClaimerTests(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<string> Linux = new(StringComparer.Ordinal) { "linux", "x64" };
    private static readonly HashSet<string> Http = new(StringComparer.Ordinal) { "http_request" };
    private static readonly HashSet<string> Nothing = new(StringComparer.Ordinal);

    // The SQLite claimer's semaphore holds no handle, so the tests do not dispose it.
    private readonly IJobClaimer claimer = provider == DatabaseProvider.Postgres ? new PostgresJobClaimer() : new SqliteJobClaimer();

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await Database.MigrateAsync(cancellationToken: Cancel);
    }

    [Fact]
    public async Task The_oldest_due_attempt_that_fits_is_offered_with_a_lease()
    {
        var job = await JobAsync();
        var later = await AttemptAsync(job, 1, queued: Now.AddMinutes(-1), labels: ["linux"]);
        var waiting = await AttemptAsync(job, 2, queued: Now.AddMinutes(-9), notBefore: Now.AddMinutes(1));
        var needsHttp = await AttemptAsync(job, 3, queued: Now.AddMinutes(-8), nodeTypes: ["http_request"]);
        var needsContainer = await AttemptAsync(job, 4, queued: Now.AddMinutes(-7), container: true);
        var oldest = await AttemptAsync(job, 5, queued: Now.AddMinutes(-5));
        var runner = Guid.CreateVersion7();

        var first = await ClaimAsync(runner, Linux, Nothing, containers: false);
        var second = await ClaimAsync(runner, Linux, Nothing, containers: false);
        var none = await ClaimAsync(runner, Linux, Nothing, containers: false);

        first.ShouldBe(oldest);
        second.ShouldBe(later);
        none.ShouldBeNull();
        var offered = await ReadAsync(oldest);
        offered.Status.ShouldBe(AttemptStatus.Offered);
        offered.RunnerId.ShouldBe(runner);
        offered.LeaseExpiresAt.ShouldBe(Now.AddMinutes(1));
        (await ClaimAsync(runner, Linux, Http, containers: true)).ShouldBe(needsHttp);
        (await ClaimAsync(runner, Linux, Http, containers: true)).ShouldBe(needsContainer);
        (await ReadAsync(waiting)).Status.ShouldBe(AttemptStatus.Queued);
    }

    [Fact]
    public async Task Concurrent_claims_never_offer_an_attempt_twice()
    {
        var job = await JobAsync();
        for (var i = 1; i <= 30; i++)
        {
            await AttemptAsync(job, i, queued: Now.AddSeconds(-i));
        }

        var claimed = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            var mine = new List<Guid>();
            var runner = Guid.CreateVersion7();
            while (await ClaimAsync(runner, Nothing, Nothing, containers: true) is { } id)
            {
                mine.Add(id);
            }

            return mine;
        })));

        var all = claimed.SelectMany(ids => ids).ToList();
        all.Count.ShouldBe(30);
        all.ShouldBeUnique();
    }

    [Fact]
    public void Requirements_are_written_sorted_and_without_duplicates() =>
        JobRequirements.Write(["x64", "linux", "x64"]).ShouldBe("""["linux","x64"]""");

    [Fact]
    public async Task The_provider_registration_brings_its_claimer()
    {
        var services = new ServiceCollection();
        _ = Provider == DatabaseProvider.Postgres ? services.AddAutoCanvasPostgres("Host=db") : services.AddAutoCanvasSqlite("Data Source=x.db");
        await using var built = services.BuildServiceProvider();

        built.GetRequiredService<IJobClaimer>().ShouldBeOfType(claimer.GetType());
    }

    private async Task<Guid?> ClaimAsync(Guid runner, IReadOnlySet<string> labels, IReadOnlySet<string> nodeTypes, bool containers)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await claimer.ClaimAsync(context, new JobClaim(runner, labels, nodeTypes, containers, Now, Now.AddMinutes(1)), Cancel);
        }
    }

    private async Task<Guid> JobAsync()
    {
        var folder = new Folder { Id = Guid.CreateVersion7(), ParentKey = Guid.Empty, Name = "Team", NormalizedName = "TEAM", Path = "/x/", CreatedAt = Now };
        var run = new Run
        {
            Id = Guid.CreateVersion7(),
            FolderId = folder.Id,
            AutomationName = "flow",
            AutomationVersion = 1,
            Number = 1,
            Yaml = "version: 1",
            Trigger = RunTriggerKind.Manual,
            TriggeredByName = "test",
            CreatedAt = Now,
        };
        var job = new RunJob { Id = Guid.CreateVersion7(), RunId = run.Id, Position = 0, Key = "build", Name = "build" };
        var context = Database.CreateContext();
        await using (context)
        {
            folder.Path = $"/{folder.Id}/";
            context.AddRange(folder, run, job);
            await context.SaveChangesAsync(Cancel);
        }

        return job.Id;
    }

    private async Task<Guid> AttemptAsync(
        Guid job, int number, DateTimeOffset queued, DateTimeOffset? notBefore = null, string[]? labels = null, string[]? nodeTypes = null, bool container = false)
    {
        var attempt = new JobAttempt
        {
            Id = Guid.CreateVersion7(),
            RunJobId = job,
            Number = number,
            QueuedAt = queued,
            NotBefore = notBefore ?? queued,
            Labels = JobRequirements.Write(labels ?? []),
            NodeTypes = number % 2 == 0 && nodeTypes is null ? string.Empty : JobRequirements.Write(nodeTypes ?? []),
            NeedsContainer = container,
            TimeoutMinutes = 60,
        };
        var context = Database.CreateContext();
        await using (context)
        {
            context.JobAttempts.Add(attempt);
            await context.SaveChangesAsync(Cancel);
        }

        return attempt.Id;
    }

    private async Task<JobAttempt> ReadAsync(Guid attempt)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await context.JobAttempts.AsNoTracking().SingleAsync(a => a.Id == attempt, Cancel);
        }
    }
}

public sealed class JobClaimerTestsOnSqlite() : JobClaimerTests(DatabaseProvider.Sqlite);

public sealed class JobClaimerTestsOnPostgres() : JobClaimerTests(DatabaseProvider.Postgres);
