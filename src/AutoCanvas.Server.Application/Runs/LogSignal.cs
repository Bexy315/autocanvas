using AutoCanvas.Persistence.Queue;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Wakes readers that follow a log on this server as soon as lines arrive. Lines stored by another
/// server are found by the readers' periodic look, so a missed signal only delays them.
/// </summary>
public sealed class LogSignal
{
    private readonly LocalSignal signal = new();

    /// <summary>Tells the waiting readers that lines arrived.</summary>
    public void Pulse() => signal.Pulse();

    /// <summary>Waits for lines or until <paramref name="timeout"/>.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => signal.WaitAsync(timeout, cancellationToken);
}
