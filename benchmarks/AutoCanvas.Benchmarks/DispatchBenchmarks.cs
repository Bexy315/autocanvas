using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Sqlite;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Benchmarks;

/// <summary>
/// Dispatch latency (ADR-0005): from saving a queued attempt to a waiting dispatcher having claimed
/// it — save, notify, wake, claim. SQLite always; PostgreSQL when <c>AUTOCANVAS_BENCH_POSTGRES</c>
/// holds a connection string to an empty database the benchmark may migrate, fill and drop.
/// </summary>
public class DispatchBenchmarks : IDisposable
{
    private static readonly HashSet<string> Nothing = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _listening = new();
    private string? _sqliteFile;
    private Func<AutoCanvasDbContext> _createContext = null!;
    private IJobClaimer _claimer = null!;
    private IJobSignal _signal = null!;
    private Guid _job;
    private int _number;

    public static IEnumerable<string> Providers =>
        Environment.GetEnvironmentVariable("AUTOCANVAS_BENCH_POSTGRES") is { Length: > 0 } ? ["Sqlite", "Postgres"] : ["Sqlite"];

    [ParamsSource(nameof(Providers))]
    public string Provider { get; set; } = "Sqlite";

    [GlobalSetup]
    public async Task SetupAsync()
    {
        if (string.Equals(Provider, "Postgres", StringComparison.Ordinal))
        {
            var connectionString = Environment.GetEnvironmentVariable("AUTOCANVAS_BENCH_POSTGRES")!;
            var builder = new DbContextOptionsBuilder<PostgresAutoCanvasDbContext>();
            builder.UseAutoCanvasPostgres(connectionString);
            _createContext = () => new PostgresAutoCanvasDbContext(builder.Options);
            _claimer = new PostgresJobClaimer();
            _signal = new PostgresJobSignal(connectionString);
        }
        else
        {
            _sqliteFile = Path.Combine(Path.GetTempPath(), $"autocanvas-bench-{Guid.NewGuid():N}.db");
            var builder = new DbContextOptionsBuilder<SqliteAutoCanvasDbContext>();
            builder.UseAutoCanvasSqlite($"Data Source={_sqliteFile}");
            _createContext = () => new SqliteAutoCanvasDbContext(builder.Options);
            _claimer = new SqliteJobClaimer();
            _signal = new SqliteJobSignal();
        }

        var context = _createContext();
        await using (context.ConfigureAwait(false))
        {
            await new DatabaseMigrator(context).MigrateAsync(CancellationToken.None).ConfigureAwait(false);
            _job = await JobAsync(context).ConfigureAwait(false);
        }

        _ = _signal.ListenAsync(_listening.Token);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _listening.CancelAsync().ConfigureAwait(false);
        var context = _createContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    [Benchmark]
    public async Task<Guid> QueueToClaim()
    {
        var now = DateTimeOffset.UtcNow;
        var woken = _signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        var writer = _createContext();
        await using (writer.ConfigureAwait(false))
        {
            writer.JobAttempts.Add(new JobAttempt
            {
                Id = Guid.CreateVersion7(),
                RunJobId = _job,
                Number = Interlocked.Increment(ref _number),
                QueuedAt = now,
                NotBefore = now,
                Labels = "[]",
                NodeTypes = "[]",
                NeedsContainer = false,
                TimeoutMinutes = 60,
            });
            await writer.SaveChangesAsync().ConfigureAwait(false);
            await _signal.NotifyAsync(writer, CancellationToken.None).ConfigureAwait(false);
        }

        if (!await woken.ConfigureAwait(false))
        {
            throw new InvalidOperationException("The dispatcher was not woken.");
        }

        var dispatcher = _createContext();
        await using (dispatcher.ConfigureAwait(false))
        {
            var claim = new JobClaim(Guid.Empty, Nothing, Nothing, RunsContainers: true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1));
            return await _claimer.ClaimAsync(dispatcher, claim, CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Nothing was claimed.");
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _listening.Dispose();
        (_claimer as IDisposable)?.Dispose();
        if (_sqliteFile is not null)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(_sqliteFile);
        }
    }

    private static async Task<Guid> JobAsync(AutoCanvasDbContext context)
    {
        var folderId = Guid.CreateVersion7();
        var run = new Run
        {
            Id = Guid.CreateVersion7(),
            FolderId = folderId,
            AutomationName = "bench",
            AutomationVersion = 1,
            Number = 1,
            Yaml = "version: 1",
            Trigger = RunTriggerKind.Manual,
            TriggeredByName = "bench",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var job = new RunJob { Id = Guid.CreateVersion7(), RunId = run.Id, Position = 0, Key = "job", Name = "job" };
        context.AddRange(
            new Folder { Id = folderId, ParentKey = Guid.Empty, Name = "Bench", NormalizedName = "BENCH", Path = $"/{folderId}/", CreatedAt = DateTimeOffset.UtcNow },
            run,
            job);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return job.Id;
    }
}
