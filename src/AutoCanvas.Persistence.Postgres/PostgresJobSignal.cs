using AutoCanvas.Persistence.Queue;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoCanvas.Persistence.Postgres;

/// <summary>
/// Signals through PostgreSQL's <c>LISTEN/NOTIFY</c> (ADR-0010), so a job queued by one server wakes
/// the dispatchers of every server. Each server keeps one connection listening.
/// </summary>
public sealed class PostgresJobSignal(string connectionString) : IJobSignal
{
    /// <summary>The notification channel.</summary>
    public const string Channel = "autocanvas_jobs";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly LocalSignal signal = new();

    /// <inheritdoc/>
    public async Task NotifyAsync(AutoCanvasDbContext database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        // This server's own waiters are woken directly, also while its listener reconnects.
        signal.Pulse();
        await database.Database.ExecuteSqlAsync($"SELECT pg_notify({Channel}, '')", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => signal.WaitAsync(timeout, cancellationToken);

    /// <inheritdoc/>
    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var connection = new NpgsqlConnection(connectionString);
                await using (connection.ConfigureAwait(false))
                {
                    connection.Notification += (_, _) => signal.Pulse();
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    var listen = new NpgsqlCommand($"LISTEN {Channel}", connection);
                    await using (listen.ConfigureAwait(false))
                    {
                        await listen.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    // Whatever was queued while not listening is picked up by waiters now.
                    signal.Pulse();
                    while (true)
                    {
                        await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
            {
                // The database went away for a moment; waiters still wake up on their timeouts.
                await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
