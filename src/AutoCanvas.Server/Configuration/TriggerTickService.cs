using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Triggers;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Every few seconds, starts the runs of due schedule entries and of automations that follow a run
/// that finished (<see cref="TriggerDispatcher"/>). Starts once the server has.
/// </summary>
internal sealed partial class TriggerTickService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<TriggerTickService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

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

        // The cadence follows the wall clock on purpose. What is due is decided with the injected
        // TimeProvider inside the round; a test clock moved ahead by days would otherwise replay one
        // round per 5 seconds it skipped.
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed round is repeated with the next tick; it must not stop the server.
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if ((await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().GetPendingAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return;
            }

            var dispatcher = scope.ServiceProvider.GetRequiredService<TriggerDispatcher>();
            Report(await dispatcher.StartScheduledAsync(cancellationToken).ConfigureAwait(false));
            Report(await dispatcher.StartFollowersAsync(cancellationToken).ConfigureAwait(false));
        }
    }

    private void Report(TriggerRound round)
    {
        foreach (var run in round.Started)
        {
            LogStarted(logger, run);
        }

        foreach (var reason in round.Skipped)
        {
            LogSkipped(logger, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A trigger started run {Run}.")]
    private static partial void LogStarted(ILogger logger, string run);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A trigger started nothing: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Starting the runs of triggers failed; it is tried again shortly.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
