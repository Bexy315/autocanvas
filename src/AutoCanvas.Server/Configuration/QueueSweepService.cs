using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Runs;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Every few seconds, returns offers nobody accepted to the queue and fails jobs whose runner went
/// quiet or that ran out of time (<see cref="JobDispatcher.SweepAsync"/>). Starts once the server has.
/// </summary>
internal sealed partial class QueueSweepService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<QueueSweepService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

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

        // The cadence follows the wall clock on purpose. What counts as expired is decided with the
        // injected TimeProvider inside the sweep; a test clock moved ahead by days would otherwise
        // replay one sweep per 15 seconds it skipped.
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed sweep is repeated with the next tick; it must not stop the server.
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if ((await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().GetPendingAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return;
            }

            var changed = await scope.ServiceProvider.GetRequiredService<JobDispatcher>().SweepAsync(cancellationToken).ConfigureAwait(false);
            if (changed > 0)
            {
                LogSwept(logger, changed);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Returned or failed {Count} job attempts whose runner did not respond in time.")]
    private static partial void LogSwept(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sweeping the job queue failed; it is tried again shortly.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
