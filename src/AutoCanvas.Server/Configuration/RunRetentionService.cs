using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Runs;

namespace AutoCanvas.Server.Configuration;

/// <summary>Applies the run retention once the server has started, and then once a day.</summary>
internal sealed partial class RunRetentionService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<RunRetentionService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not while the server is still starting: a start that fails must not leave this job
        // working on the database.
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

        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await PruneAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed run is retried the next day; it must not stop the server.
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if ((await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().GetPendingAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return;
            }

            var deleted = await scope.ServiceProvider.GetRequiredService<RunRetention>().PruneAsync(cancellationToken).ConfigureAwait(false);
            if (deleted > 0)
            {
                LogPruned(logger, deleted);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} runs older than the retention period, with their logs and reports.")]
    private static partial void LogPruned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Applying the run retention failed; it is tried again tomorrow.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
