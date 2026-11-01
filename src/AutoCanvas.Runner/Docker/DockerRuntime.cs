using System.Net;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace AutoCanvas.Runner.Docker;

/// <summary>
/// <see cref="IContainerRuntime"/> on the Docker Engine API, found as the <c>docker</c> command line
/// finds it: <c>DOCKER_HOST</c>, the active context, or the platform's default socket.
/// </summary>
/// <remarks>
/// Containers get <c>no-new-privileges</c> and a process limit, and never the Docker socket:
/// whoever holds it is root on the machine and could read every other job's secrets.
/// </remarks>
internal sealed class DockerRuntime(IDockerClient docker) : IContainerRuntime, IDisposable
{
    /// <summary>Processes one container may have; enough for builds, not for a fork bomb.</summary>
    public const long PidsLimit = 4096;

    public DockerRuntime()
        : this(new DockerClientBuilder().Build())
    {
    }

    public async Task PullAsync(string image, Func<string, Task> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        try
        {
            await docker.Images.InspectImageAsync(image, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Not there yet: pull it.
        }

        var reference = ImageReference.Parse(image);
        await progress($"Pulling {image}").ConfigureAwait(false);
        await docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = reference.Repository, Tag = reference.Tag },
            null,
            new Progress<JSONMessage>(),
            cancellationToken).ConfigureAwait(false);
        await progress($"Pulled {image}").ConfigureAwait(false);
    }

    public async Task<string> StartAsync(ContainerSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var created = await docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = spec.Image,
                Cmd = [.. spec.Command],
                Env = [.. spec.Env.Select(e => $"{e.Key}={e.Value}")],
                Labels = new Dictionary<string, string>(spec.Labels, StringComparer.Ordinal),
                HostConfig = new HostConfig
                {
                    Binds = [.. spec.Binds],
                    SecurityOpt = ["no-new-privileges"],
                    PidsLimit = PidsLimit,
                },
            },
            cancellationToken).ConfigureAwait(false);
        await docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken).ConfigureAwait(false);
        return created.ID;
    }

    public async Task<long> ExecAsync(string containerId, ExecSpec exec, Func<OutputLine, Task> output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exec);
        ArgumentNullException.ThrowIfNull(output);
        var created = await docker.Exec.CreateContainerExecAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                Cmd = [.. exec.Command],
                WorkingDir = exec.WorkingDirectory,
                Env = [.. exec.Env.Select(e => $"{e.Key}={e.Value}")],
            },
            cancellationToken).ConfigureAwait(false);
        using (var stream = await docker.Exec.StartContainerExecAsync(created.ID, new ContainerExecStartParameters(), cancellationToken).ConfigureAwait(false))
        {
            await PumpAsync(stream, output, cancellationToken).ConfigureAwait(false);
        }

        var inspected = await docker.Exec.InspectContainerExecAsync(created.ID, cancellationToken).ConfigureAwait(false);
        return inspected.ExitCode ?? -1;
    }

    public Task RestartAsync(string containerId, CancellationToken cancellationToken) =>
        docker.Containers.RestartContainerAsync(containerId, new ContainerRestartParameters { WaitBeforeKillSeconds = 1 }, cancellationToken);

    public async Task RemoveAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await docker.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerContainerNotFoundException)
        {
            // Gone already.
        }
    }

    public async Task<int> RemoveLabelledAsync(string label, string value, CancellationToken cancellationToken)
    {
        var containers = await docker.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal)
                {
                    ["label"] = new Dictionary<string, bool>(StringComparer.Ordinal) { [$"{label}={value}"] = true },
                },
            },
            cancellationToken).ConfigureAwait(false);
        foreach (var container in containers)
        {
            await RemoveAsync(container.ID, cancellationToken).ConfigureAwait(false);
        }

        return containers.Count;
    }

    public void Dispose() => docker.Dispose();

    /// <summary>Splits the multiplexed output into lines, keeping a partial line until it ends.</summary>
    private static async Task PumpAsync(MultiplexedStream stream, Func<OutputLine, Task> output, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var lines = new Dictionary<bool, LineSplitter> { [false] = new(), [true] = new() };
        while (true)
        {
            var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read.EOF)
            {
                break;
            }

            var isError = read.Target == MultiplexedStream.TargetStream.StandardError;
            foreach (var line in lines[isError].Add(buffer.AsSpan(0, read.Count)))
            {
                await output(new OutputLine(isError, line)).ConfigureAwait(false);
            }
        }

        foreach (var (isError, splitter) in lines)
        {
            if (splitter.Rest() is { } rest)
            {
                await output(new OutputLine(isError, rest)).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Turns UTF-8 bytes into lines, across reads that end in the middle of a line or a character.</summary>
    private sealed class LineSplitter
    {
        private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder pending = new();

        public List<string> Add(ReadOnlySpan<byte> bytes)
        {
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            var count = decoder.GetChars(bytes, chars, flush: false);
            var lines = new List<string>();
            foreach (var c in chars.AsSpan(0, count))
            {
                if (c == '\n')
                {
                    lines.Add(pending.ToString().TrimEnd('\r'));
                    pending.Clear();
                }
                else
                {
                    pending.Append(c);
                }
            }

            return lines;
        }

        public string? Rest() => pending.Length == 0 ? null : pending.ToString().TrimEnd('\r');
    }
}
