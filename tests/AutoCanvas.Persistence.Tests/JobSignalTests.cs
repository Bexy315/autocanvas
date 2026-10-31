using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Sqlite;
using AutoCanvas.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Persistence.Tests;

public sealed class LocalSignalTests
{
    [Fact]
    public async Task A_pulse_wakes_everyone_waiting_at_that_moment()
    {
        var signal = new LocalSignal();
        var first = signal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var second = signal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        signal.Pulse();

        (await first).ShouldBeTrue();
        (await second).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_pulse_waiting_ends_with_the_timeout()
    {
        var signal = new LocalSignal();
        signal.Pulse();

        (await signal.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken)).ShouldBeFalse();
    }
}

public abstract class JobSignalTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task A_notification_wakes_the_dispatchers_of_every_server()
    {
        // Two signals stand for two servers on the same database.
        var here = Create();
        var there = Create();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        var listening = there.ListenAsync(stop.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Cancel);
        var postgres = Provider == DatabaseProvider.Postgres;
        var waiting = there.WaitAsync(postgres ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(200), Cancel);
        var waitingHere = here.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        var context = Database.CreateContext();
        await using (context)
        {
            await here.NotifyAsync(context, Cancel);
        }

        (await waitingHere).ShouldBeTrue();
        (await waiting).ShouldBe(postgres, "only PostgreSQL has other servers");
        await stop.CancelAsync();
        try
        {
            await listening;
        }
        catch (OperationCanceledException)
        {
            // How PostgreSQL's listener ends; SQLite's returned at once.
        }

        listening.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task The_provider_registration_brings_its_signal()
    {
        var services = new ServiceCollection();
        _ = Provider == DatabaseProvider.Postgres ? services.AddAutoCanvasPostgres("Host=db") : services.AddAutoCanvasSqlite("Data Source=x.db");
        await using var built = services.BuildServiceProvider();

        built.GetRequiredService<IJobSignal>().ShouldBeOfType(Create().GetType());
    }

    private IJobSignal Create() => Provider == DatabaseProvider.Postgres ? new PostgresJobSignal(Database.ConnectionString) : new SqliteJobSignal();
}

public sealed class JobSignalTestsOnSqlite() : JobSignalTests(DatabaseProvider.Sqlite);

public sealed class JobSignalTestsOnPostgres() : JobSignalTests(DatabaseProvider.Postgres);
