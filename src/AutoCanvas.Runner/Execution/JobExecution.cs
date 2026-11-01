using System.Threading.Channels;
using AutoCanvas.Core.Secrets;
using AutoCanvas.Protocol.Runner.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// One accepted job, from preparation to <c>JobFinished</c> (runner protocol, <em>Job lifecycle</em>).
/// The connection hands it the server's instructions; it runs steps one at a time in the order they
/// come, and remembers its last state message so that a reconnect can send it again.
/// </summary>
internal sealed partial class JobExecution : IDisposable
{
    private static readonly SecretRedactor NoSecrets = new([]);

    private readonly Job job;
    private readonly JobServices services;
    private readonly Guid runnerId;
    private readonly ReportUploader reports;
    private readonly ILogger logger;
    private readonly JobPaths paths;
    private readonly Channel<Step?> instructions = Channel.CreateUnbounded<Step?>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource cancelled = new();
    private readonly Lock gate = new();
    private int lastDelivered = -1;
    private ConnectRequest? lastState;
    private string? cancelReason;

    public JobExecution(Job job, JobServices services, Guid runnerId, ReportUploader reports, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(services);
        this.job = job;
        this.services = services;
        this.runnerId = runnerId;
        this.reports = reports;
        this.logger = logger;
        paths = new JobPaths(services.Options, job.JobId);
    }

    public string Id => job.JobId;

    /// <summary>The last state message sent, for a reconnect.</summary>
    public ConnectRequest? LastState
    {
        get
        {
            lock (gate)
            {
                return lastState;
            }
        }
    }

    /// <summary>A step to run; one the job has seen already is ignored.</summary>
    public void Deliver(Step step)
    {
        ArgumentNullException.ThrowIfNull(step);
        lock (gate)
        {
            if ((int)step.Index <= lastDelivered)
            {
                return;
            }

            lastDelivered = (int)step.Index;
        }

        instructions.Writer.TryWrite(step);
    }

    /// <summary>There are no more steps: collect reports, clean up, finish.</summary>
    public void Complete() => instructions.Writer.TryWrite(null);

    /// <summary>Stops the running step, then finishes as after <see cref="Complete"/>.</summary>
    public void Cancel(string reason)
    {
        lock (gate)
        {
            cancelReason ??= reason;
        }

        cancelled.Cancel();
        instructions.Writer.TryWrite(null);
    }

    /// <summary>Runs the job to its end.</summary>
    /// <param name="abort">Gives up at once, when the runner stops without waiting.</param>
    /// <returns><c>JobFinished</c>, as sent.</returns>
    public async Task<ConnectRequest> RunAsync(CancellationToken abort)
    {
        var log = new LogBatcher(job.JobId, services.Outbox, services.Time);
        var warnings = new List<string>();
        await using (log.ConfigureAwait(false))
        {
            var environment = new JobEnvironment(job, services, paths, runnerId, log);
            try
            {
                var error = await environment.PrepareAsync(abort).ConfigureAwait(false);
                log.Flush();
                Send(new ConnectRequest { JobPrepared = new JobPrepared { JobId = job.JobId, Error = error ?? string.Empty } });
                await StepsAsync(error is null ? environment.ContainerId : null, log, abort).ConfigureAwait(false);
                if (cancelReason is { } reason)
                {
                    log.Write(null, LogStream.System, $"The job was cancelled: {reason}", NoSecrets);
                }

                warnings.AddRange(await reports.UploadAsync(job, paths.Workspace, abort).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (abort.IsCancellationRequested)
            {
                warnings.Add("The runner stopped before the job was done.");
            }
            finally
            {
                var problems = await environment.CleanUpAsync().ConfigureAwait(false);
                if (problems.Count > 0)
                {
                    LogCleanupFailed(logger, job.JobId, string.Join(" ", problems));
                }

                warnings.AddRange(problems);
            }

            if (log.Dropped > 0)
            {
                warnings.Add($"{log.Dropped} log chunk(s) were dropped while the server was out of reach.");
            }
        }

        var finished = new JobFinished { JobId = job.JobId };
        finished.Warnings.Add(warnings);
        var message = new ConnectRequest { JobFinished = finished };
        Send(message);
        return message;
    }

    public void Dispose() => cancelled.Dispose();

    /// <summary>Runs the steps the server sends until it completes the job; without a container, none run.</summary>
    private async Task StepsAsync(string? containerId, LogBatcher log, CancellationToken abort)
    {
        var scripts = containerId is null ? null : new ScriptStep(services, paths, containerId, log);
        var nodes = containerId is null ? null : new NodeSteps(services, paths, runnerId, job.JobId, log);
        await foreach (var step in instructions.Reader.ReadAllAsync(abort).ConfigureAwait(false))
        {
            if (step is null)
            {
                return;
            }

            if (scripts is null || nodes is null || cancelled.IsCancellationRequested)
            {
                continue;
            }

            Send(new ConnectRequest { StepStarted = new StepStarted { JobId = job.JobId, StepIndex = step.Index, StartedAt = Timestamp.FromDateTimeOffset(services.Time.GetUtcNow()) } });
            var completed = step.ActionCase == Step.ActionOneofCase.Node
                ? await nodes.RunAsync(step, cancelled.Token, abort).ConfigureAwait(false)
                : await scripts.RunAsync(step, cancelled.Token, abort).ConfigureAwait(false);
            completed.JobId = job.JobId;
            completed.FinishedAt ??= Timestamp.FromDateTimeOffset(services.Time.GetUtcNow());
            if (completed.Outcome == StepOutcome.Errored)
            {
                log.Write((int)step.Index, LogStream.System, completed.Error, NoSecrets);
            }

            log.Flush();
            Send(new ConnectRequest { StepCompleted = completed });
        }
    }

    private void Send(ConnectRequest message)
    {
        lock (gate)
        {
            lastState = message;
        }

        services.Outbox.Send(message);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up after job {JobId} failed: {Problems}")]
    private static partial void LogCleanupFailed(ILogger logger, string jobId, string problems);
}
