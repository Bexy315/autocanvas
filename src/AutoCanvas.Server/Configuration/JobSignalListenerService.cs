using AutoCanvas.Persistence.Queue;

namespace AutoCanvas.Server.Configuration;

/// <summary>Keeps <see cref="IJobSignal.ListenAsync"/> running, so jobs queued by other servers wake this one's dispatchers.</summary>
internal sealed class JobSignalListenerService(IJobSignal signal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await signal.ListenAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The server stops.
        }
    }
}
