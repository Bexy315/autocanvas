using AutoCanvas.Core.Secrets;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Docker;
using Docker.DotNet;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Everything a job runs in, from preparation to cleanup (runner protocol, <em>Prepare</em>): the
/// workspace, a network of its own when it has services, its services until they are ready, the
/// job's container, and the checkouts, cloned by git in a helper container.
/// </summary>
internal sealed class JobEnvironment(Job job, JobServices services, JobPaths paths, Guid runnerId, LogBatcher log)
{
    /// <summary>How many lines of a service's own log are shown when it does not get ready.</summary>
    public const int ServiceLogLines = 20;

    private static readonly SecretRedactor NoSecrets = new([]);

    private readonly List<string> started = [];
    private string? network;

    /// <summary>The job's container, once it runs.</summary>
    public string? ContainerId { get; private set; }

    /// <returns>Why the job cannot run, or <see langword="null"/> once it is ready.</returns>
    public async Task<string?> PrepareAsync(CancellationToken abort)
    {
        try
        {
            Directory.CreateDirectory(paths.Workspace);
            Directory.CreateDirectory(paths.Meta);
            if (job.Services.Count > 0)
            {
                network = await services.Runtime.CreateNetworkAsync($"autocanvas-{job.JobId}", ContainerLabels.For(runnerId, job.JobId), abort).ConfigureAwait(false);
                foreach (var service in job.Services)
                {
                    await StartServiceAsync(service, abort).ConfigureAwait(false);
                }
            }

            var image = string.IsNullOrEmpty(job.Image) ? services.Options.DefaultImage : job.Image;
            await PullAsync(image, abort).ConfigureAwait(false);
            ContainerId = await StartAsync(image, Idle, paths.Binds, new Dictionary<string, string>(job.Env, StringComparer.Ordinal), network, null, abort).ConfigureAwait(false);
            Write($"Started the job's container from {image}.");
            if (await CheckOutAsync(abort).ConfigureAwait(false) is { } failed)
            {
                return failed;
            }

            foreach (var (service, id) in job.Services.Zip(started))
            {
                if (await WaitUntilReadyAsync(service, id, abort).ConfigureAwait(false) is { } notReady)
                {
                    return notReady;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is DockerApiException or IOException or HttpRequestException or UnauthorizedAccessException)
        {
            var error = $"The runner could not prepare the job: {exception.Message}";
            Write(error);
            return error;
        }
    }

    /// <summary>Empties and removes the containers, the network and the job's directory; problems become warnings.</summary>
    public async Task<List<string>> CleanUpAsync()
    {
        var warnings = new List<string>();
        using var patience = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            if (ContainerId is { } container)
            {
                // The container's processes run as whoever the image says, often root: files they
                // made in the workspace are removed from inside, where the runner's user may not.
                await services.Runtime.ExecAsync(container, Quiet(["sh", "-c", "find /workspace /autocanvas -mindepth 1 -delete"]), _ => Task.CompletedTask, patience.Token).ConfigureAwait(false);
                await services.Runtime.RemoveAsync(container, patience.Token).ConfigureAwait(false);
            }

            foreach (var service in started)
            {
                await services.Runtime.RemoveAsync(service, patience.Token).ConfigureAwait(false);
            }

            if (network is not null)
            {
                await services.Runtime.RemoveNetworkAsync(network, patience.Token).ConfigureAwait(false);
            }

            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is DockerApiException or IOException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
        {
            warnings.Add($"The runner could not clean up after the job: {exception.Message}");
        }

        return warnings;
    }

    /// <summary>A shell script that keeps a container alive until it is stopped, and ends at once on SIGTERM.</summary>
    public const string IdleScript = "trap 'exit 0' TERM; while :; do sleep 3600 & wait $!; done";

    /// <summary>The command that runs <see cref="IdleScript"/>.</summary>
    public static IReadOnlyList<string> Idle { get; } = ["sh", "-c", IdleScript];

    private static ExecSpec Quiet(IReadOnlyList<string> command) => new(command, JobPaths.ContainerWorkspace, new Dictionary<string, string>(StringComparer.Ordinal));

    private async Task StartServiceAsync(Service service, CancellationToken abort)
    {
        await PullAsync(service.Image, abort).ConfigureAwait(false);
        var id = await StartAsync(service.Image, null, [], new Dictionary<string, string>(service.Env, StringComparer.Ordinal), network, service.Name, abort).ConfigureAwait(false);
        started.Add(id);
        Write($"Started service {service.Name} from {service.Image}.");
    }

    private async Task<string?> WaitUntilReadyAsync(Service service, string id, CancellationToken abort)
    {
        if (string.IsNullOrEmpty(service.ReadyCommand))
        {
            return null;
        }

        var until = services.Time.GetUtcNow() + (service.ReadyTimeout?.ToTimeSpan() ?? TimeSpan.FromMinutes(2));
        while (await services.Runtime.IsRunningAsync(id, abort).ConfigureAwait(false))
        {
            // In /: a service's container has no workspace, and an exec in a missing directory fails.
            var check = new ExecSpec(["sh", "-c", service.ReadyCommand], "/", new Dictionary<string, string>(StringComparer.Ordinal));
            if (await services.Runtime.ExecAsync(id, check, _ => Task.CompletedTask, abort).ConfigureAwait(false) == 0)
            {
                Write($"Service {service.Name} is ready.");
                return null;
            }

            if (services.Time.GetUtcNow() >= until)
            {
                return await NotReadyAsync(service, id, "did not get ready in time", abort).ConfigureAwait(false);
            }

            await Task.Delay(services.ReadyInterval, abort).ConfigureAwait(false);
        }

        return await NotReadyAsync(service, id, "stopped before it got ready", abort).ConfigureAwait(false);
    }

    private async Task<string> NotReadyAsync(Service service, string id, string what, CancellationToken abort)
    {
        var error = $"Service {service.Name} {what}.";
        Write(error);
        foreach (var line in await services.Runtime.LogTailAsync(id, ServiceLogLines, abort).ConfigureAwait(false))
        {
            Write($"[{service.Name}] {line}");
        }

        return error;
    }

    /// <returns>Why a checkout failed, or <see langword="null"/>.</returns>
    private async Task<string?> CheckOutAsync(CancellationToken abort)
    {
        if (job.Checkouts.Count == 0)
        {
            return null;
        }

        await PullAsync(services.Options.GitImage, abort).ConfigureAwait(false);
        // The image's entrypoint is git; the helper idles instead and git runs in it per command.
        var git = await StartAsync(services.Options.GitImage, ["-c", IdleScript], paths.Binds, new Dictionary<string, string>(StringComparer.Ordinal), null, null, abort, entrypoint: ["sh"]).ConfigureAwait(false);
        try
        {
            foreach (var checkout in job.Checkouts)
            {
                if (await CloneAsync(git, checkout, abort).ConfigureAwait(false) is { } failed)
                {
                    return failed;
                }
            }

            return null;
        }
        finally
        {
            await services.Runtime.RemoveAsync(git, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<string?> CloneAsync(string git, Checkout checkout, CancellationToken abort)
    {
        var target = JobPaths.InContainer(checkout.Path);
        if (target is null)
        {
            return Fail($"The checkout of {checkout.Url} would land outside the workspace ('{checkout.Path}').");
        }

        var redactor = new SecretRedactor(CheckoutCommand.Secrets(checkout));
        Write($"Checking out {checkout.Url}{(checkout.Ref.Length > 0 ? $" at {checkout.Ref}" : string.Empty)} into {target}.");
        foreach (var command in CheckoutCommand.Commands(checkout, target))
        {
            var exec = new ExecSpec(command, JobPaths.ContainerWorkspace, CheckoutCommand.Environment(checkout));
            var exitCode = await services.Runtime.ExecAsync(git, exec, line =>
            {
                log.Write(null, line.IsError ? LogStream.Stderr : LogStream.Stdout, line.Text, redactor);
                return Task.CompletedTask;
            }, abort).ConfigureAwait(false);
            if (exitCode != 0)
            {
                var verb = string.Equals(command[1], "-C", StringComparison.Ordinal) ? command[3] : command[1];
                return Fail($"Checking out {checkout.Url} failed: git {verb} exited with {exitCode}.");
            }
        }

        return null;
    }

    private string Fail(string error)
    {
        Write(error);
        return error;
    }

    private Task PullAsync(string image, CancellationToken abort) => services.Runtime.PullAsync(image, line =>
    {
        Write(line);
        return Task.CompletedTask;
    }, abort);

    private Task<string> StartAsync(
        string image,
        IReadOnlyList<string>? command,
        IReadOnlyList<string> binds,
        Dictionary<string, string> env,
        string? onNetwork,
        string? alias,
        CancellationToken abort,
        IReadOnlyList<string>? entrypoint = null) =>
        services.Runtime.StartAsync(
            new ContainerSpec(image, command ?? [], binds, env, ContainerLabels.For(runnerId, job.JobId))
            {
                Entrypoint = entrypoint,
                Network = onNetwork,
                NetworkAlias = alias,
            },
            abort);

    private void Write(string line) => log.Write(null, LogStream.System, line, NoSecrets);
}
