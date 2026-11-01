using System.Threading.Channels;
using AutoCanvas.Core.Secrets;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Docker;
using Docker.DotNet;
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
        string? containerId = null;
        await using (log.ConfigureAwait(false))
        {
            try
            {
                (containerId, var error) = await PrepareAsync(log, abort).ConfigureAwait(false);
                log.Flush();
                Send(new ConnectRequest { JobPrepared = new JobPrepared { JobId = job.JobId, Error = error ?? string.Empty } });
                await StepsAsync(error is null ? new ScriptStep(services, paths, containerId!, log) : null, log, abort).ConfigureAwait(false);
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
                warnings.AddRange(await CleanUpAsync(containerId).ConfigureAwait(false));
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

    private async Task<(string? ContainerId, string? Error)> PrepareAsync(LogBatcher log, CancellationToken abort)
    {
        try
        {
            Directory.CreateDirectory(paths.Workspace);
            Directory.CreateDirectory(paths.Meta);
            var image = string.IsNullOrEmpty(job.Image) ? services.Options.DefaultImage : job.Image;
            await services.Runtime.PullAsync(image, line => Setup(log, line), abort).ConfigureAwait(false);
            var id = await services.Runtime.StartAsync(
                new ContainerSpec(
                    image,
                    ["sh", "-c", "trap 'exit 0' TERM; while :; do sleep 3600 & wait $!; done"],
                    paths.Binds,
                    new Dictionary<string, string>(job.Env, StringComparer.Ordinal),
                    ContainerLabels.For(runnerId, job.JobId)),
                abort).ConfigureAwait(false);
            await Setup(log, $"Started the job's container from {image}.").ConfigureAwait(false);
            return (id, null);
        }
        catch (Exception exception) when (exception is DockerApiException or IOException or HttpRequestException or UnauthorizedAccessException)
        {
            var error = $"The runner could not prepare the job: {exception.Message}";
            log.Write(null, LogStream.System, error, NoSecrets);
            return (null, error);
        }
    }

    private async Task StepsAsync(ScriptStep? steps, LogBatcher log, CancellationToken abort)
    {
        await foreach (var step in instructions.Reader.ReadAllAsync(abort).ConfigureAwait(false))
        {
            if (step is null)
            {
                return;
            }

            if (steps is null || cancelled.IsCancellationRequested)
            {
                continue;
            }

            Send(new ConnectRequest { StepStarted = new StepStarted { JobId = job.JobId, StepIndex = step.Index, StartedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(services.Time.GetUtcNow()) } });
            var completed = await steps.RunAsync(step, cancelled.Token, abort).ConfigureAwait(false);
            completed.JobId = job.JobId;
            completed.FinishedAt ??= Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(services.Time.GetUtcNow());
            if (completed.Outcome == StepOutcome.Errored)
            {
                log.Write((int)step.Index, LogStream.System, completed.Error, NoSecrets);
            }

            log.Flush();
            Send(new ConnectRequest { StepCompleted = completed });
        }
    }

    /// <summary>Empties and removes the container and the job's directory; problems become warnings.</summary>
    private async Task<List<string>> CleanUpAsync(string? containerId)
    {
        var warnings = new List<string>();
        using var patience = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            if (containerId is not null)
            {
                // The container's processes run as whoever the image says, often root: files they
                // made in the workspace are removed from inside, where the runner's user may not.
                var empty = new ExecSpec(["sh", "-c", "find /workspace /autocanvas -mindepth 1 -delete"], JobPaths.ContainerWorkspace, new Dictionary<string, string>(StringComparer.Ordinal));
                await services.Runtime.ExecAsync(containerId, empty, _ => Task.CompletedTask, patience.Token).ConfigureAwait(false);
                await services.Runtime.RemoveAsync(containerId, patience.Token).ConfigureAwait(false);
            }

            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is DockerApiException or IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
        {
            LogCleanupFailed(logger, job.JobId, exception);
            warnings.Add($"The runner could not clean up after the job: {exception.Message}");
        }

        return warnings;
    }

    private void Send(ConnectRequest message)
    {
        lock (gate)
        {
            lastState = message;
        }

        services.Outbox.Send(message);
    }

    private static Task Setup(LogBatcher log, string line)
    {
        log.Write(null, LogStream.System, line, NoSecrets);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up after job {JobId} failed")]
    private static partial void LogCleanupFailed(ILogger logger, string jobId, Exception exception);
}
