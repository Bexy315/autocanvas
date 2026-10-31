namespace AutoCanvas.Persistence.Queue;

/// <summary>
/// Wakes the dispatchers that hand jobs to runners as soon as an attempt is queued, instead of
/// letting them poll (ADR-0010: <c>LISTEN/NOTIFY</c> on PostgreSQL, in process on SQLite). A missed
/// signal only delays a job until the waiter's timeout; it never loses one.
/// </summary>
public interface IJobSignal
{
    /// <summary>Tells every waiting dispatcher, on every server, that attempts may be due. Call it after saving them.</summary>
    Task NotifyAsync(AutoCanvasDbContext database, CancellationToken cancellationToken);

    /// <summary>Waits until notified or until <paramref name="timeout"/>; returns whether it was notified.</summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Receives notifications from other servers until cancelled; the host runs it in the background.
    /// Returns at once where there are no other servers.
    /// </summary>
    Task ListenAsync(CancellationToken cancellationToken);
}
