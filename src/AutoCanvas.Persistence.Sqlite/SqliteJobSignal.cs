using AutoCanvas.Persistence.Queue;

namespace AutoCanvas.Persistence.Sqlite;

/// <summary>A SQLite installation is one server process, so the signal never leaves it (ADR-0010).</summary>
public sealed class SqliteJobSignal : IJobSignal
{
    private readonly LocalSignal signal = new();

    /// <inheritdoc/>
    public Task NotifyAsync(AutoCanvasDbContext database, CancellationToken cancellationToken)
    {
        signal.Pulse();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => signal.WaitAsync(timeout, cancellationToken);

    /// <inheritdoc/>
    public Task ListenAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
