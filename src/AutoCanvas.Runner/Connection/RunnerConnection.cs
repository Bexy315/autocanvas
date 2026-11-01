using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Execution;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Runner.Connection;

/// <summary>
/// The runner's side of the control stream (runner protocol, <em>Connection</em>): it connects out,
/// says Hello, sends heartbeats, takes offers while it has room, passes instructions to the jobs,
/// and connects again with <see cref="Backoff"/> when the stream breaks — the jobs run on meanwhile.
/// On shutdown it drains: no new jobs, running ones get <see cref="RunnerOptions.ShutdownTimeout"/>
/// to finish.
/// </summary>
internal sealed partial class RunnerConnection(
    ServerChannel server,
    Outbox outbox,
    JobRegistry jobs,
    JobFactory factory,
    JobServices services,
    LeftoverSweeper sweeper,
    IOptions<RunnerOptions> options,
    IHostApplicationLifetime lifetime,
    RunnerExitCode exitCode,
    ILogger<RunnerConnection> logger) : BackgroundService
{
    private readonly Backoff backoff = new();
    private readonly CancellationTokenSource abort = new();
    private readonly List<Task> running = [];
    private volatile bool draining;
    private TimeSpan? reconnectAfter;

    public override void Dispose()
    {
        abort.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting: stopping it from inside its start would fail the start.
        await Task.Yield();
        var credential = await RunnerCredential.ReadAsync(options.Value.CredentialFile, stoppingToken).ConfigureAwait(false);
        if (credential is null)
        {
            LogNotRegistered(logger, options.Value.CredentialFile);
            Stop(RunnerExitCode.NotAuthorized);
            return;
        }

        await sweeper.SweepAsync(credential.RunnerId, stoppingToken).ConfigureAwait(false);
        using var connection = new CancellationTokenSource();
        var loop = ConnectAsync(credential, connection.Token);
        try
        {
            await loop.WaitAsync(stoppingToken).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: drain while the stream stays open.
        }

        await DrainAsync().ConfigureAwait(false);

        // The last messages leave, then the stream is closed from this side and the server ends it.
        outbox.Close();
        await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None)).ConfigureAwait(false);
        await connection.CancelAsync().ConfigureAwait(false);
        await Ended(loop).ConfigureAwait(false);
    }

    private static async Task Ended(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose.
        }
    }

    private async Task ConnectAsync(RunnerCredential credential, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var again = await SessionAsync(credential, cancellationToken).ConfigureAwait(false);
                if (outbox.IsClosed)
                {
                    return;
                }

                if (!again)
                {
                    LogToldToStop(logger);
                    Stop(0);
                    return;
                }
            }
            catch (RpcException) when (cancellationToken.IsCancellationRequested)
            {
                // The runner stops; gRPC reports the cancelled call as an error of its own.
                return;
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated)
            {
                LogRevoked(logger, exception.Status.Detail);
                Stop(RunnerExitCode.NotAuthorized);
                return;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is RpcException or IOException or HttpRequestException or InvalidOperationException)
            {
                LogConnectionLost(logger, exception.Message);
            }

            var wait = reconnectAfter ?? backoff.Next();
            reconnectAfter = null;
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <returns>Whether to connect again when the stream ends.</returns>
    private async Task<bool> SessionAsync(RunnerCredential credential, CancellationToken cancellationToken)
    {
        using var call = server.Runner.Connect(ServerChannel.Authorization(credential), cancellationToken: cancellationToken);
        var hello = new Hello { ProtocolVersion = RunnerCapabilities.ProtocolVersion, Capabilities = RunnerCapabilities.Of(options.Value) };
        hello.ActiveJobIds.Add(jobs.Ids);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = hello }, cancellationToken).ConfigureAwait(false);
        if (!await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            throw new IOException("The server closed the stream before it said Welcome.");
        }

        var first = call.ResponseStream.Current;
        if (first.MessageCase == ConnectResponse.MessageOneofCase.Goodbye)
        {
            return Farewell(first.Goodbye);
        }

        var welcome = first.Welcome ?? throw new IOException($"The server answered Hello with {first.MessageCase} instead of Welcome.");
        backoff.Reset();
        LogConnected(logger, welcome.RunnerId);
        foreach (var message in jobs.Replay())
        {
            outbox.Send(message);
        }

        if (draining)
        {
            outbox.Send(new ConnectRequest { Draining = new Draining() });
        }

        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var writing = WriteAsync(call.RequestStream, session.Token);
        var beating = HeartbeatAsync(welcome.HeartbeatInterval.ToTimeSpan(), session.Token);
        try
        {
            return await ReadAsync(call.ResponseStream, credential, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.CancelAsync().ConfigureAwait(false);
            await Ended(Task.WhenAll(writing, beating)).ConfigureAwait(false);
        }
    }

    private async Task<bool> ReadAsync(IAsyncStreamReader<ConnectResponse> responses, RunnerCredential credential, CancellationToken cancellationToken)
    {
        while (await responses.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            var message = responses.Current;
            switch (message.MessageCase)
            {
                case ConnectResponse.MessageOneofCase.JobOffer:
                    Offered(message.JobOffer.Job, credential);
                    break;
                case ConnectResponse.MessageOneofCase.RunStep when jobs.TryGet(message.RunStep.JobId, out var job):
                    job.Deliver(message.RunStep.Step);
                    break;
                case ConnectResponse.MessageOneofCase.CompleteJob when jobs.TryGet(message.CompleteJob.JobId, out var job):
                    job.Complete();
                    break;
                case ConnectResponse.MessageOneofCase.CancelJob when jobs.TryGet(message.CancelJob.JobId, out var job):
                    job.Cancel(message.CancelJob.Reason);
                    break;
                case ConnectResponse.MessageOneofCase.Goodbye:
                    return Farewell(message.Goodbye);
                default:
                    // Instructions for jobs this runner no longer holds, and messages it does not know.
                    break;
            }
        }

        return true;
    }

    private void Offered(Job job, RunnerCredential credential)
    {
        if (draining || jobs.Count >= options.Value.MaxConcurrentJobs)
        {
            outbox.Send(new ConnectRequest { JobRejected = new JobRejected { JobId = job.JobId, Reason = draining ? "The runner is shutting down." : "The runner is at capacity." } });
            return;
        }

        if (jobs.TryGet(job.JobId, out _))
        {
            // An offer of a job the runner has taken already, sent again after a reconnect.
            return;
        }

        var execution = factory.Create(job, credential);
        jobs.TryAdd(execution);
        outbox.Send(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job.JobId } });
        LogAccepted(logger, job.JobId, job.AutomationName, job.JobKey);
        lock (running)
        {
            running.RemoveAll(t => t.IsCompleted);
            running.Add(RunAsync(execution));
        }
    }

    private async Task RunAsync(JobExecution job)
    {
        using (job)
        {
            // Off the reading loop: a job's preparation must not hold up the stream.
            await Task.Yield();
            var finished = await job.RunAsync(abort.Token).ConfigureAwait(false);
            jobs.Finish(job, finished);
            LogFinished(logger, job.Id);
        }
    }

    private async Task WriteAsync(IClientStreamWriter<ConnectRequest> requests, CancellationToken cancellationToken)
    {
        await foreach (var message in outbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await requests.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            outbox.Taken(message);
        }

        // Closed: everything is sent.
        await requests.CompleteAsync().ConfigureAwait(false);
    }

    // The interval ticks on the wall clock, as the server expects heartbeats in real time.
    private async Task HeartbeatAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval > TimeSpan.Zero ? interval : TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var heartbeat = new Heartbeat();
            heartbeat.ActiveJobIds.Add(jobs.Ids);
            outbox.Send(new ConnectRequest { Heartbeat = heartbeat });
        }
    }

    private bool Farewell(Goodbye goodbye)
    {
        LogGoodbye(logger, goodbye.Reason);
        reconnectAfter = goodbye.ReconnectAfter?.ToTimeSpan();
        return goodbye.Reconnect;
    }

    /// <summary>Takes no new jobs, lets running ones finish for a while, then cancels the rest.</summary>
    private async Task DrainAsync()
    {
        draining = true;
        outbox.Send(new ConnectRequest { Draining = new Draining() });
        LogDraining(logger, jobs.Count);
        if (!await jobs.WhenEmptyAsync(options.Value.ShutdownTimeout).ConfigureAwait(false))
        {
            foreach (var job in jobs.Jobs)
            {
                job.Cancel("The runner is shutting down.");
            }

            if (!await jobs.WhenEmptyAsync(services.GracePeriod * 3).ConfigureAwait(false))
            {
                await abort.CancelAsync().ConfigureAwait(false);
            }
        }

        Task[] all;
        lock (running)
        {
            all = [.. running];
        }

        await Task.WhenAny(Task.WhenAll(all), Task.Delay(TimeSpan.FromMinutes(1))).ConfigureAwait(false);
    }

    private void Stop(int code)
    {
        exitCode.Value = code;
        lifetime.StopApplication();
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "The runner is not registered: {CredentialFile} does not exist. Run 'autocanvas-runner register' first.")]
    private static partial void LogNotRegistered(ILogger logger, string credentialFile);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The server refused the runner's credential ({Reason}). The runner was deleted or its credential revoked; register it again.")]
    private static partial void LogRevoked(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to the server as runner {RunnerId}")]
    private static partial void LogConnected(ILogger logger, string runnerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the connection to the server: {Reason}")]
    private static partial void LogConnectionLost(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "The server said goodbye: {Reason}")]
    private static partial void LogGoodbye(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The server told the runner to stop")]
    private static partial void LogToldToStop(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Took job {JobId} ({Automation}, {JobKey})")]
    private static partial void LogAccepted(ILogger logger, string jobId, string automation, string jobKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finished job {JobId}")]
    private static partial void LogFinished(ILogger logger, string jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutting down: waiting for {Count} job(s) to finish")]
    private static partial void LogDraining(ILogger logger, int count);
}
