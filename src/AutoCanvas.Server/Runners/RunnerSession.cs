using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Application.Runs;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// One runner's control stream (runner protocol v1, ADR-0017): the handshake, then a loop that
/// reads the runner's messages and a loop that offers it jobs while it has room. Everything the
/// session decides is stored, so a runner can reconnect to any server; the session itself only
/// remembers what the runner announced on this stream.
/// </summary>
/// <remarks>
/// A gRPC stream allows one writer at a time, so both loops queue their messages in an outbox
/// that a third loop writes. Every database access uses its own scope: a stream lasts for hours.
/// </remarks>
internal sealed partial class RunnerSession(
    Guid runnerId,
    IServiceScopeFactory scopes,
    IJobSignal signal,
    TimeSpan heartbeatInterval,
    TimeSpan leaseDuration,
    ILogger logger)
{
    /// <summary>The protocol version the server uses on every stream.</summary>
    public const string ProtocolVersion = "1.0";

    private readonly Channel<ConnectResponse> outbox = Channel.CreateUnbounded<ConnectResponse>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    // Attempts this stream has told to stop, so that CancelJob goes out once per stream.
    private readonly ConcurrentDictionary<Guid, bool> cancelSent = new();
    private RunnerCapabilities capabilities = null!;
    private volatile bool draining;

    /// <summary>How long a runner waits before it reconnects after the server stopped.</summary>
    public static readonly TimeSpan ReconnectAfterShutdown = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Serves the stream until the runner closes it, the server says goodbye or the call ends. When
    /// <paramref name="serverStopping"/> fires, the runner is told to reconnect later.
    /// </summary>
    public async Task RunAsync(IAsyncStreamReader<ConnectRequest> requests, IServerStreamWriter<ConnectResponse> responses, CancellationToken serverStopping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(responses);
        var hello = await ReadHelloAsync(requests, cancellationToken).ConfigureAwait(false);
        var runner = await ConnectAsync(hello, cancellationToken).ConfigureAwait(false);
        await responses.WriteAsync(new ConnectResponse
        {
            Welcome = new Welcome
            {
                ProtocolVersion = ProtocolVersion,
                HeartbeatInterval = Duration.FromTimeSpan(heartbeatInterval),
                LeaseDuration = Duration.FromTimeSpan(leaseDuration),
                RunnerId = runnerId.ToString(),
            },
        }, cancellationToken).ConfigureAwait(false);
        LogConnected(logger, runner.Name, runnerId, hello.ProtocolVersion);

        var continuing = await RenewAsync(hello.ActiveJobIds, "The job was given up while the runner was away.", cancellationToken).ConfigureAwait(false);
        await ResumeAsync(continuing, cancellationToken).ConfigureAwait(false);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var shutdown = serverStopping.Register(() => SayGoodbye("The server is shutting down.", ReconnectAfterShutdown));
        var writing = WriteAsync(responses, stop.Token);
        var reading = ReadAsync(requests, stop.Token);
        var offering = OfferAsync(stop.Token);
        try
        {
            // The runner closed its side, or the outbox was closed after a goodbye.
            await (await Task.WhenAny(reading, writing).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            outbox.Writer.TryComplete();
            await IgnoreCancellationAsync(reading, writing, offering).ConfigureAwait(false);
            LogDisconnected(logger, runner.Name, runnerId);
        }
    }

    private static async Task<Hello> ReadHelloAsync(IAsyncStreamReader<ConnectRequest> requests, CancellationToken cancellationToken)
    {
        if (!await requests.MoveNext(cancellationToken).ConfigureAwait(false) || requests.Current.MessageCase != ConnectRequest.MessageOneofCase.Hello)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message on the stream must be Hello."));
        }

        var hello = requests.Current.Hello;
        var major = hello.ProtocolVersion.Split('.')[0];
        return string.Equals(major, "1", StringComparison.Ordinal)
            ? hello
            : throw new RpcException(new Status(StatusCode.FailedPrecondition, $"This server speaks runner protocol {ProtocolVersion}; the runner speaks '{hello.ProtocolVersion}'."));
    }

    private async Task<RunnerState> ConnectAsync(Hello hello, CancellationToken cancellationToken)
    {
        var announced = ProtocolMapping.ToCapabilities(hello.Capabilities);
        if (announced.Error is { } invalid)
        {
            throw ProtocolMapping.ToRpcException(invalid);
        }

        capabilities = announced.Value!;
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var connected = await scope.ServiceProvider.GetRequiredService<RunnerConnectionService>().ConnectAsync(runnerId, capabilities, cancellationToken).ConfigureAwait(false);
            return connected.Value ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "The runner was deleted."));
        }
    }

    private async Task WriteAsync(IServerStreamWriter<ConnectResponse> responses, CancellationToken cancellationToken)
    {
        await foreach (var message in outbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await responses.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Send(ConnectResponse message) => outbox.Writer.TryWrite(message);

    /// <summary>
    /// Says goodbye and closes the outbox, which ends the stream once it is written. Without
    /// <paramref name="reconnectAfter"/> the runner must not come back.
    /// </summary>
    private void SayGoodbye(string reason, TimeSpan? reconnectAfter = null)
    {
        var goodbye = new Goodbye { Reason = reason, Reconnect = reconnectAfter is not null };
        if (reconnectAfter is { } delay)
        {
            goodbye.ReconnectAfter = Duration.FromTimeSpan(delay);
        }

        Send(new ConnectResponse { Goodbye = goodbye });
        outbox.Writer.TryComplete();
    }

    /// <summary>Makes the offer loop look again now; several wake-ups before it looks count once.</summary>
    private void Wake() => wake.Writer.TryWrite(true);

    private async Task ReadAsync(IAsyncStreamReader<ConnectRequest> requests, CancellationToken cancellationToken)
    {
        while (await requests.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            var message = requests.Current;
            switch (message.MessageCase)
            {
                case ConnectRequest.MessageOneofCase.Heartbeat:
                    await HeartbeatAsync(message.Heartbeat, cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectRequest.MessageOneofCase.JobAccepted:
                    await AcceptAsync(message.JobAccepted.JobId, cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectRequest.MessageOneofCase.JobRejected:
                    await RejectAsync(message.JobRejected, cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectRequest.MessageOneofCase.Draining:
                    draining = true;
                    LogDraining(logger, runnerId);
                    break;
                case ConnectRequest.MessageOneofCase.JobPrepared:
                    await PreparedAsync(message.JobPrepared, cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectRequest.MessageOneofCase.StepStarted:
                    await StepStartedAsync(message.StepStarted, cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectRequest.MessageOneofCase.StepCompleted:
                    await StepCompletedAsync(message.StepCompleted, cancellationToken).ConfigureAwait(false);
                    break;
                case ConnectRequest.MessageOneofCase.JobFinished:
                    await FinishedAsync(message.JobFinished, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    // Logs are stored from S2 step 5 on; messages this server does not know are
                    // ignored (protocol versioning).
                    break;
            }
        }
    }

    private async Task HeartbeatAsync(Heartbeat heartbeat, CancellationToken cancellationToken)
    {
        await RenewAsync(heartbeat.ActiveJobIds, "The runner no longer holds this job.", cancellationToken).ConfigureAwait(false);
        await SendCancellationsAsync(cancellationToken).ConfigureAwait(false);
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if (await scope.ServiceProvider.GetRequiredService<RunnerConnectionService>().HeartbeatAsync(runnerId, cancellationToken).ConfigureAwait(false) is null)
            {
                SayGoodbye(DeletedReason);
                return;
            }
        }

        // A runner that was resumed takes jobs again without waiting for the next look.
        Wake();
    }

    /// <summary>
    /// Renews the leases of the jobs the runner lists and cancels the ones it no longer holds.
    /// </summary>
    /// <returns>The jobs it still holds.</returns>
    private async Task<IReadOnlyList<Guid>> RenewAsync(RepeatedField<string> jobIds, string reason, CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0)
        {
            return [];
        }

        var ids = jobIds.Select(ProtocolMapping.ToId).OfType<Guid>().ToList();
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var givenUp = (await scope.ServiceProvider.GetRequiredService<JobDispatcher>().RenewAsync(runnerId, ids, cancellationToken).ConfigureAwait(false)).ToHashSet();
            foreach (var jobId in jobIds.Where(id => ProtocolMapping.ToId(id) is not { } parsed || givenUp.Contains(parsed)))
            {
                Send(new ConnectResponse { CancelJob = new Protocol.Runner.V1.CancelJob { JobId = jobId, Reason = reason } });
            }

            return [.. ids.Where(id => !givenUp.Contains(id))];
        }
    }

    /// <summary>
    /// After a reconnect, sends each job the runner still holds whatever it may have missed: the
    /// step it was sent, or <c>CompleteJob</c>. A runner ignores a step it has already started.
    /// </summary>
    private async Task ResumeAsync(IReadOnlyList<Guid> attemptIds, CancellationToken cancellationToken)
    {
        foreach (var attemptId in attemptIds)
        {
            await ProgressAsync(progress => progress.PendingAsync(attemptId, runnerId, cancellationToken)).ConfigureAwait(false);
        }
    }

    private Task PreparedAsync(JobPrepared prepared, CancellationToken cancellationToken) =>
        ProtocolMapping.ToId(prepared.JobId) is { } attemptId
            ? ProgressAsync(progress => progress.PreparedAsync(attemptId, runnerId, prepared.Error, cancellationToken))
            : Task.CompletedTask;

    private async Task StepStartedAsync(StepStarted started, CancellationToken cancellationToken)
    {
        if (ProtocolMapping.ToId(started.JobId) is not { } attemptId)
        {
            return;
        }

        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scope.ServiceProvider.GetRequiredService<JobProgress>()
                .StepStartedAsync(attemptId, runnerId, (int)Math.Min(started.StepIndex, int.MaxValue), started.StartedAt?.ToDateTimeOffset(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task StepCompletedAsync(StepCompleted completed, CancellationToken cancellationToken) =>
        ProtocolMapping.ToId(completed.JobId) is { } attemptId
            ? ProgressAsync(progress => progress.StepCompletedAsync(attemptId, runnerId, ProtocolMapping.ToReport(completed), cancellationToken))
            : Task.CompletedTask;

    private async Task FinishedAsync(JobFinished finished, CancellationToken cancellationToken)
    {
        if (ProtocolMapping.ToId(finished.JobId) is not { } attemptId)
        {
            return;
        }

        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scope.ServiceProvider.GetRequiredService<JobProgress>().FinishedAsync(attemptId, runnerId, [.. finished.Warnings], cancellationToken).ConfigureAwait(false);
        }

        cancelSent.TryRemove(attemptId, out _);

        // The runner has room again.
        Wake();
    }

    /// <summary>Runs a progress use case in its own scope and sends the instruction it returns.</summary>
    private async Task ProgressAsync(Func<JobProgress, Task<JobInstruction?>> action)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            switch (await action(scope.ServiceProvider.GetRequiredService<JobProgress>()).ConfigureAwait(false))
            {
                case RunStepInstruction run:
                    Send(new ConnectResponse { RunStep = new Protocol.Runner.V1.RunStep { JobId = run.AttemptId.ToString(), Step = ProtocolMapping.ToStep(run.Step) } });
                    break;
                case CompleteJobInstruction complete:
                    Send(new ConnectResponse { CompleteJob = new CompleteJob { JobId = complete.AttemptId.ToString() } });
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Tells the runner to stop the jobs that were cancelled or ran out of time, once per stream.</summary>
    private async Task SendCancellationsAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            foreach (var (attemptId, reason) in await scope.ServiceProvider.GetRequiredService<JobProgress>().CancellationsAsync(runnerId, cancellationToken).ConfigureAwait(false))
            {
                if (cancelSent.TryAdd(attemptId, true))
                {
                    Send(new ConnectResponse { CancelJob = new Protocol.Runner.V1.CancelJob { JobId = attemptId.ToString(), Reason = reason } });
                }
            }
        }
    }

    private async Task AcceptAsync(string jobId, CancellationToken cancellationToken)
    {
        var accepted = false;
        if (ProtocolMapping.ToId(jobId) is { } attemptId)
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                accepted = await scope.ServiceProvider.GetRequiredService<JobDispatcher>().AcceptAsync(attemptId, runnerId, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!accepted)
        {
            // The offer ran out, and the job may already be with another runner.
            Send(new ConnectResponse { CancelJob = new Protocol.Runner.V1.CancelJob { JobId = jobId, Reason = "The offer was withdrawn before it was accepted." } });
        }
    }

    private async Task RejectAsync(JobRejected rejected, CancellationToken cancellationToken)
    {
        if (ProtocolMapping.ToId(rejected.JobId) is not { } attemptId)
        {
            return;
        }

        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if (await scope.ServiceProvider.GetRequiredService<JobDispatcher>().RejectAsync(attemptId, runnerId, cancellationToken).ConfigureAwait(false))
            {
                LogRejected(logger, runnerId, attemptId, rejected.Reason);
            }
        }

        Wake();
    }

    private async Task OfferAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Cancelling a run or a time limit wakes this loop through the queue's signal.
                await SendCancellationsAsync(cancellationToken).ConfigureAwait(false);
                if (await TryOfferAsync(cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The next look tries again; a database hiccup must not end the stream.
                LogOfferFailed(logger, runnerId, exception);
            }

            await WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Offers the runner one job if it may take one; returns whether it should look again at once.</summary>
    private async Task<bool> TryOfferAsync(CancellationToken cancellationToken)
    {
        if (draining)
        {
            return false;
        }

        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var services = scope.ServiceProvider;
            var state = await services.GetRequiredService<RunnerConnectionService>().StateAsync(runnerId, cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                SayGoodbye(DeletedReason);
                return false;
            }

            var dispatcher = services.GetRequiredService<JobDispatcher>();
            if (state.IsPaused || await dispatcher.CountHeldAsync(runnerId, cancellationToken).ConfigureAwait(false) >= capabilities.MaxConcurrentJobs)
            {
                return false;
            }

            var labels = capabilities.Labels.ToHashSet(StringComparer.Ordinal);
            var nodeTypes = capabilities.NodeTypes.ToHashSet(StringComparer.Ordinal);
            if (await dispatcher.ClaimAsync(runnerId, labels, nodeTypes, capabilities.RunsContainers, cancellationToken).ConfigureAwait(false) is not { } attemptId)
            {
                return false;
            }

            // Draining may have been announced while the claim was made.
            if (draining)
            {
                await dispatcher.RejectAsync(attemptId, runnerId, cancellationToken).ConfigureAwait(false);
                return false;
            }

            var offer = await services.GetRequiredService<JobOfferBuilder>().BuildAsync(attemptId, cancellationToken).ConfigureAwait(false);
            if (offer.Error is { } error)
            {
                await dispatcher.FinishAsync(attemptId, runnerId, AttemptOutcome.Failed, error.Message, cancellationToken).ConfigureAwait(false);
                return true;
            }

            Send(new ConnectResponse { JobOffer = new JobOffer { Job = ProtocolMapping.ToJob(offer.Value!) } });
            LogOffered(logger, runnerId, attemptId);
            return true;
        }
    }

    /// <summary>Waits for queued work, for a message that frees room, or for one heartbeat interval.</summary>
    private async Task WaitAsync(CancellationToken cancellationToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waiting.CancelAfter(heartbeatInterval);
        var signalled = signal.WaitAsync(heartbeatInterval, waiting.Token);
        var woken = wake.Reader.ReadAsync(waiting.Token).AsTask();
        await Task.WhenAny(signalled, woken).ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);
        await IgnoreCancellationAsync(signalled, woken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until every task has ended — none may still use the database when the session is over —
    /// and then rethrows the first failure that is not the stream or the session stopping.
    /// </summary>
    private static async Task IgnoreCancellationAsync(params Task[] tasks)
    {
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        var failure = tasks
            .Where(t => t.IsFaulted)
            .SelectMany(t => t.Exception!.InnerExceptions)
            .FirstOrDefault(e => e is not (OperationCanceledException or IOException));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    private const string DeletedReason = "The runner was deleted on the server.";

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {Name} ({RunnerId}) connected with protocol {Version}.")]
    private static partial void LogConnected(ILogger logger, string name, Guid runnerId, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {Name} ({RunnerId}) disconnected.")]
    private static partial void LogDisconnected(ILogger logger, string name, Guid runnerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {RunnerId} is draining and takes no new jobs.")]
    private static partial void LogDraining(ILogger logger, Guid runnerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Offered job {AttemptId} to runner {RunnerId}.")]
    private static partial void LogOffered(ILogger logger, Guid runnerId, Guid attemptId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runner {RunnerId} rejected job {AttemptId}: {Reason}")]
    private static partial void LogRejected(ILogger logger, Guid runnerId, Guid attemptId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Offering a job to runner {RunnerId} failed; it is tried again shortly.")]
    private static partial void LogOfferFailed(ILogger logger, Guid runnerId, Exception exception);
}
