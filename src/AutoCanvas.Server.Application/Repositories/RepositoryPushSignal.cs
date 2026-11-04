namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// Wakes the server's background handling of pushes when a webhook stored one, so that its runs
/// start at once rather than at the next round. Other servers find it at their next round.
/// </summary>
public sealed class RepositoryPushSignal : IDisposable
{
    private readonly SemaphoreSlim pending = new(0, 1);

    /// <summary>Says that a push waits to be handled.</summary>
    public void Notify()
    {
        try
        {
            pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled; one wake-up handles every waiting push.
        }
    }

    /// <summary>Waits until a push was stored or <paramref name="timeout"/> passed.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => pending.WaitAsync(timeout, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => pending.Dispose();
}
