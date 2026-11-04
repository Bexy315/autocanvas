using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Repositories;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Every half minute, polls the connected repositories that are due (<see cref="RepositoryPolling"/>)
/// and starts the runs their pushes call for. Starts once the server has.
/// </summary>
internal sealed partial class RepositoryPollService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<RepositoryPollService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
        {
            try
            {
                await started.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // As for the triggers, the cadence follows the wall clock; what is due is decided with the
        // injected TimeProvider inside the round.
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PollAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed round is repeated with the next tick; it must not stop the server.
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if ((await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().GetPendingAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return;
            }

            var round = await scope.ServiceProvider.GetRequiredService<RepositoryPolling>().PollDueAsync(cancellationToken).ConfigureAwait(false);
            foreach (var run in round.Started)
            {
                LogStarted(logger, run);
            }

            foreach (var reason in round.Skipped)
            {
                LogSkipped(logger, reason);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A push started run {Run}.")]
    private static partial void LogStarted(ILogger logger, string run);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A poll started nothing: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling the repositories failed; it is tried again shortly.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
