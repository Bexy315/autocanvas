using System.Collections.Concurrent;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Application.Runs;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// The control streams this server serves. When the server stops, every runner is told to
/// reconnect shortly — to this server once it is back, or to another one — and the server waits
/// until the sessions have ended, so none of them still works on the database.
/// </summary>
internal sealed class RunnerSessions(
    IServiceScopeFactory scopes,
    IJobSignal signal,
    IOptions<RunnerOptions> runners,
    IOptions<QueueOptions> queue,
    IHostApplicationLifetime lifetime,
    ILogger<RunnerSession> logger) : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<Guid, Task> running = new();
    private readonly CancellationTokenSource stopping = new();
    private CancellationTokenRegistration onStopping;
    private int stopped;

    /// <summary>Serves one control stream of the runner until it ends.</summary>
    public async Task RunAsync(Guid runnerId, IAsyncStreamReader<ConnectRequest> requests, IServerStreamWriter<ConnectResponse> responses, CancellationToken cancellationToken)
    {
        // Counted before it starts: with SQLite the database calls complete synchronously, so the
        // session's task may only exist after its handshake has used the database.
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = Guid.NewGuid();
        running[key] = ended.Task;
        try
        {
            if (Volatile.Read(ref stopped) == 1)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "The server is shutting down."));
            }

            var session = new RunnerSession(runnerId, scopes, signal, runners.Value.HeartbeatInterval, queue.Value.LeaseDuration, logger);
            await session.RunAsync(requests, responses, stopping.Token, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            running.TryRemove(key, out _);
            ended.SetResult();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Runners hear the goodbye as soon as the server starts to stop, before the hosted
        // services are stopped one by one.
        onStopping = lifetime.ApplicationStopping.Register(stopping.Cancel);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // A host that was stopped and disposed can be asked to stop again (the test host is).
        if (Interlocked.Exchange(ref stopped, 1) == 1)
        {
            return;
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(running.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or RpcException or IOException)
        {
            // Sessions that ended with an error, or a shutdown that does not wait any longer.
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref stopped, 1);
        onStopping.Dispose();
        stopping.Dispose();
    }
}
