using System.Diagnostics;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Triggers;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Handles the pushes repositories' webhooks stored, as soon as one arrives and every few seconds
/// (for those other servers stored), and every half minute polls the connected repositories that are
/// due (<see cref="RepositoryPolling"/>). Starts once the server has.
/// </summary>
internal sealed partial class RepositoryPollService(
    IServiceScopeFactory scopes,
    RepositoryPushSignal signal,
    IHostApplicationLifetime lifetime,
    ILogger<RepositoryPollService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan ReceivedInterval = TimeSpan.FromSeconds(5);

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
        long? lastPoll = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var poll = lastPoll is not { } last || Stopwatch.GetElapsedTime(last) >= Interval;
            lastPoll = poll ? Stopwatch.GetTimestamp() : lastPoll;
            try
            {
                await RoundAsync(poll, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed round is repeated with the next one; it must not stop the server.
                LogFailed(logger, exception);
            }

            try
            {
                await signal.WaitAsync(ReceivedInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RoundAsync(bool poll, CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if ((await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().GetPendingAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return;
            }

            var polling = scope.ServiceProvider.GetRequiredService<RepositoryPolling>();
            Report(await polling.HandleReceivedAsync(cancellationToken).ConfigureAwait(false));
            if (poll)
            {
                Report(await polling.PollDueAsync(cancellationToken).ConfigureAwait(false));
            }

            // What changed in the runs goes back to the forges: commit statuses, pull request comments.
            Report(await scope.ServiceProvider.GetRequiredService<ForgeReporter>().ReportAsync(cancellationToken).ConfigureAwait(false));
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

    [LoggerMessage(Level = LogLevel.Information, Message = "A push started run {Run}.")]
    private static partial void LogStarted(ILogger logger, string run);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Repositories: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling the repositories failed; it is tried again shortly.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
