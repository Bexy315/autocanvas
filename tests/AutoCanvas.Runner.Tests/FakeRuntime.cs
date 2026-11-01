using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using AutoCanvas.Runner.Docker;

namespace AutoCanvas.Runner.Tests;

/// <summary>
/// A container engine without containers, for driving jobs through the runner quickly. Step scripts
/// are read from the job's directory and run by a tiny interpreter: <c>echo</c> (with <c>$VAR</c>,
/// <c>&gt;&amp;2</c> and <c>&gt;&gt; "$AUTOCANVAS_OUTPUT"</c>), <c>exit N</c>, <c>sleep N</c> (ended
/// by the runner's kill command), <c>sleep-hard N</c> (ended only by a restart) and
/// <c>write PATH TEXT</c> (a file in the workspace).
/// </summary>
internal sealed partial class FakeRuntime : IContainerRuntime
{
    private readonly ConcurrentDictionary<string, ContainerSpec> containers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> stoppable = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> hard = new(StringComparer.Ordinal);
    private int next;

    public HashSet<string> Shells { get; } = new(StringComparer.Ordinal) { "bash", "sh" };

    public ConcurrentQueue<string> Pulled { get; } = new();

    public ConcurrentQueue<string> Removed { get; } = new();

    public ConcurrentQueue<string> Restarted { get; } = new();

    public ConcurrentDictionary<string, ContainerSpec> Started { get; } = new(StringComparer.Ordinal);

    /// <summary>Images whose pull fails.</summary>
    public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

    public int Leftovers { get; set; }

    public ConcurrentQueue<string> Networks { get; } = new();

    public ConcurrentQueue<string> RemovedNetworks { get; } = new();

    /// <summary>Every command run outside the job's steps: git, BuildKit, ready checks.</summary>
    public ConcurrentQueue<ExecSpec> Helpers { get; } = new();

    /// <summary>The registry credentials BuildKit was given, as it saw them.</summary>
    public ConcurrentQueue<string> DockerConfigs { get; } = new();

    /// <summary>Images whose containers stop at once, as a crashing service would.</summary>
    public HashSet<string> Crashing { get; } = new(StringComparer.Ordinal);

    /// <summary>Programs (<c>git</c>, <c>buildctl-daemonless.sh</c>) that fail.</summary>
    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    public Task PullAsync(string image, Func<string, Task> progress, CancellationToken cancellationToken)
    {
        if (Missing.Contains(image))
        {
            throw new IOException($"pull access denied for {image}");
        }

        Pulled.Enqueue(image);
        return progress($"Pulled {image}");
    }

    public Task<string> StartAsync(ContainerSpec spec, CancellationToken cancellationToken)
    {
        var id = "container-" + Interlocked.Increment(ref next).ToString(CultureInfo.InvariantCulture);
        containers[id] = spec;
        Started[id] = spec;
        return Task.FromResult(id);
    }

    public async Task<long> ExecAsync(string containerId, ExecSpec exec, Func<OutputLine, Task> output, CancellationToken cancellationToken)
    {
        var spec = containers[containerId];
        var command = exec.Command;
        if (command[0] is "git" or "buildctl-daemonless.sh")
        {
            return await HelperAsync(spec, exec, output, cancellationToken);
        }

        if (spec.Command.Count == 0)
        {
            // A service's ready check: "false" never gets ready.
            Helpers.Enqueue(exec);
            return string.Equals(command[2], "false", StringComparison.Ordinal) ? 1 : 0;
        }

        if (command[2].StartsWith("command -v", StringComparison.Ordinal))
        {
            return Shells.Contains(command[3]) ? 0 : 1;
        }

        if (command[2].StartsWith("kill", StringComparison.Ordinal))
        {
            var number = PidFile().Match(command[2]).Groups["number"].Value;
            if (stoppable.TryGetValue($"{containerId}/{number}", out var running))
            {
                await running.CancelAsync();
            }

            return 0;
        }

        if (command[2].StartsWith("find", StringComparison.Ordinal))
        {
            return 0;
        }

        var script = await File.ReadAllLinesAsync(Host(spec, command[4]), cancellationToken);
        var env = new Dictionary<string, string>(spec.Env, StringComparer.Ordinal);
        foreach (var (key, value) in exec.Env)
        {
            env[key] = value;
        }

        return await RunAsync(spec, $"{containerId}/{command[5]}", script, env, output, cancellationToken);
    }

    public async Task RestartAsync(string containerId, CancellationToken cancellationToken)
    {
        Restarted.Enqueue(containerId);
        foreach (var (key, source) in hard.Concat(stoppable))
        {
            if (key.StartsWith(containerId + "/", StringComparison.Ordinal))
            {
                await source.CancelAsync();
            }
        }
    }

    public Task RemoveAsync(string containerId, CancellationToken cancellationToken)
    {
        containers.TryRemove(containerId, out _);
        Removed.Enqueue(containerId);
        return Task.CompletedTask;
    }

    public Task<int> RemoveLabelledAsync(string label, string value, CancellationToken cancellationToken) => Task.FromResult(Leftovers);

    public Task<string> CreateNetworkAsync(string name, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken)
    {
        Networks.Enqueue(name);
        return Task.FromResult("network-" + name);
    }

    public Task RemoveNetworkAsync(string networkId, CancellationToken cancellationToken)
    {
        RemovedNetworks.Enqueue(networkId);
        return Task.CompletedTask;
    }

    public Task<bool> IsRunningAsync(string containerId, CancellationToken cancellationToken) =>
        Task.FromResult(!Crashing.Contains(containers[containerId].Image));

    public Task<IReadOnlyList<string>> LogTailAsync(string containerId, int lines, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([$"{containers[containerId].Image} says goodbye"]);

    /// <summary>git makes the target directory; BuildKit writes its metadata.</summary>
    private async Task<long> HelperAsync(ContainerSpec spec, ExecSpec exec, Func<OutputLine, Task> output, CancellationToken cancellationToken)
    {
        Helpers.Enqueue(exec);
        var command = exec.Command;
        await output(new OutputLine(true, $"{command[0]} {command[1]}"));
        if (Failing.Contains(command[0]))
        {
            return 128;
        }

        if (string.Equals(command[0], "git", StringComparison.Ordinal) && command[1] is "clone" or "init")
        {
            Directory.CreateDirectory(Path.Combine(Host(spec, command[^1]), ".git"));
        }

        if (string.Equals(command[0], "buildctl-daemonless.sh", StringComparison.Ordinal))
        {
            if (exec.Env.TryGetValue("DOCKER_CONFIG", out var config))
            {
                DockerConfigs.Enqueue(await File.ReadAllTextAsync(Host(spec, config + "/config.json"), cancellationToken));
            }

            await File.WriteAllTextAsync(Host(spec, command[^1]), "{\"containerimage.digest\": \"sha256:abc\"}", cancellationToken);
        }

        return 0;
    }

    /// <summary>The runner's side of a path in the job's container, through its bind mounts.</summary>
    private static string Host(ContainerSpec spec, string path)
    {
        foreach (var bind in spec.Binds)
        {
            var split = bind.LastIndexOf(":/", StringComparison.Ordinal);
            var (host, inContainer) = (bind[..split], bind[(split + 1)..]);
            if (path.StartsWith(inContainer, StringComparison.Ordinal))
            {
                return host + path[inContainer.Length..];
            }
        }

        throw new InvalidOperationException($"{path} is not in a bind mount.");
    }

    private static string Expand(string text, Dictionary<string, string> env) =>
        Variable().Replace(text, m => env.GetValueOrDefault(m.Groups["braced"].Value + m.Groups["bare"].Value, string.Empty));

    [GeneratedRegex(@"pid-(?<number>\d+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PidFile();

    [GeneratedRegex(@"\$\{(?<braced>\w+)\}|\$(?<bare>\w+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Variable();

    private async Task<long> RunAsync(ContainerSpec spec, string key, string[] script, Dictionary<string, string> env, Func<OutputLine, Task> output, CancellationToken cancellationToken)
    {
        using var stop = new CancellationTokenSource();
        using var restart = new CancellationTokenSource();
        stoppable[key] = stop;
        hard[key] = restart;
        try
        {
            foreach (var raw in script)
            {
                var line = Expand(raw.Trim(), env);
                var parts = line.Split(' ', 3);
                switch (parts[0])
                {
                    case "exit":
                        return long.Parse(parts[1], CultureInfo.InvariantCulture);
                    case "sleep" or "sleep-hard":
                        var until = string.Equals(parts[0], "sleep", StringComparison.Ordinal) ? CancellationTokenSource.CreateLinkedTokenSource(stop.Token, restart.Token) : CancellationTokenSource.CreateLinkedTokenSource(restart.Token);
                        using (until)
                        {
                            try
                            {
                                await Task.Delay(TimeSpan.FromSeconds(double.Parse(parts[1], CultureInfo.InvariantCulture)), until.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                return 143;
                            }
                        }

                        break;
                    case "write":
                        var file = Host(spec, "/workspace/" + parts[1]);
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        await File.WriteAllTextAsync(file, parts[2], cancellationToken);
                        break;
                    case "echo":
                        await EchoAsync(spec, line[5..], env, output, cancellationToken);
                        break;
                    default:
                        break;
                }
            }

            return 0;
        }
        finally
        {
            stoppable.TryRemove(key, out _);
            hard.TryRemove(key, out _);
        }
    }

    private static async Task EchoAsync(ContainerSpec spec, string text, Dictionary<string, string> env, Func<OutputLine, Task> output, CancellationToken cancellationToken)
    {
        if (text.EndsWith(">&2", StringComparison.Ordinal))
        {
            await output(new OutputLine(true, text[..^3].Trim()));
            return;
        }

        var append = text.IndexOf(">>", StringComparison.Ordinal);
        if (append >= 0)
        {
            await File.AppendAllTextAsync(Host(spec, env["AUTOCANVAS_OUTPUT"]), text[..append].Trim().Trim('"') + "\n", cancellationToken);
            return;
        }

        await output(new OutputLine(false, text));
    }
}
