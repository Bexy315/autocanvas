namespace AutoCanvas.Persistence.Queue;

/// <summary>A signal inside one process: <see cref="Pulse"/> wakes everyone waiting at that moment.</summary>
public sealed class LocalSignal
{
    private readonly Lock gate = new();
    private TaskCompletionSource current = New();

    /// <summary>Wakes every current waiter; later waiters wait for the next pulse.</summary>
    public void Pulse()
    {
        TaskCompletionSource pulsed;
        lock (gate)
        {
            pulsed = current;
            current = New();
        }

        pulsed.TrySetResult();
    }

    /// <summary>Waits for the next pulse or until <paramref name="timeout"/>; returns whether it was pulsed.</summary>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task next;
        lock (gate)
        {
            next = current.Task;
        }

        try
        {
            await next.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
