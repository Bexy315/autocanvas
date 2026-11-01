using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Docker;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// The <c>docker_build</c> node: builds an image from the workspace with rootless BuildKit in a
/// container of its own — not privileged, without the Docker socket, seeing the job's workspace and
/// the runner's build cache — and pushes it when asked to. Without <c>push</c> the image is only
/// built, which proves that it builds and gives its digest.
/// </summary>
/// <remarks>
/// Rootless BuildKit makes namespaces of its own, which Docker's default seccomp and AppArmor
/// profiles forbid; they are lifted for this container only. It still holds no capability on the
/// host.
/// </remarks>
internal sealed class DockerBuildNode(JobServices services, JobPaths paths, Guid runnerId, string jobId)
{
    /// <summary>Where rootless BuildKit keeps its state, which the cache volume holds.</summary>
    public const string CacheDirectory = "/home/user/.local/share/buildkit";

    public async Task<NodeResult> RunAsync(NodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var build = Plan(context);
        var number = context.Step.Index.ToString(CultureInfo.InvariantCulture);
        var directory = Path.Combine(paths.Meta, $"build-{number}");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            // BuildKit runs as its own user; it writes the metadata here and reads the credentials.
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal) { ["BUILDKITD_FLAGS"] = "--oci-worker-no-process-sandbox" };
        if (build.Credentials is { } credentials)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "config.json"), credentials, context.Stop).ConfigureAwait(false);
            env["DOCKER_CONFIG"] = $"{JobPaths.ContainerMeta}/build-{number}";
        }

        try
        {
            return await BuildAsync(context, build, number, env, directory).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The image references for <paramref name="image"/> and <paramref name="tags"/>; a tag in the image counts when no tags are given.</summary>
    public static List<string> References(string image, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var reference = ImageReference.Parse(image);
        var hasTag = image.LastIndexOf(':') > image.LastIndexOf('/');
        var all = tags.Count > 0 ? tags : hasTag ? [reference.Tag] : ["latest"];
        return [.. all.Select(tag => $"{reference.Repository}:{tag}")];
    }

    /// <summary>The registry an image is pushed to: the first part of its name if that is a host, else Docker Hub.</summary>
    public static string Registry(string image)
    {
        var first = image.Split('/')[0];
        return image.Contains('/', StringComparison.Ordinal) && (first.Contains('.', StringComparison.Ordinal) || first.Contains(':', StringComparison.Ordinal) || string.Equals(first, "localhost", StringComparison.Ordinal))
            ? first
            : "https://index.docker.io/v1/";
    }

    private static BuildPlan Plan(NodeContext context)
    {
        var inputs = context.Inputs;
        var image = inputs.RequiredText("image");
        var contextDirectory = JobPaths.InContainer(inputs.Text("context"))
            ?? throw new NodeInputException("The input 'context' must be a directory inside the workspace.");
        var dockerfile = JobPaths.InContainer($"{inputs.Text("context") ?? "."}/{inputs.Text("dockerfile") ?? "Dockerfile"}")
            ?? throw new NodeInputException("The input 'dockerfile' must be a file inside the workspace.");
        var push = inputs.Boolean("push", false);
        string? credentials = null;
        if (inputs.Text("password") is { Length: > 0 } password)
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{inputs.Text("username") ?? string.Empty}:{password}"));
            credentials = JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["auths"] = new Dictionary<string, object>(StringComparer.Ordinal) { [inputs.Text("registry") ?? Registry(image)] = new Dictionary<string, string>(StringComparer.Ordinal) { ["auth"] = auth } },
            });
        }

        var arguments = new List<string>
        {
            "build", "--progress", "plain", "--frontend", "dockerfile.v0",
            "--local", $"context={contextDirectory}",
            "--local", $"dockerfile={dockerfile[..dockerfile.LastIndexOf('/')]}",
            "--opt", $"filename={dockerfile[(dockerfile.LastIndexOf('/') + 1)..]}",
        };
        foreach (var (name, value) in inputs.TextMap("build_args"))
        {
            arguments.AddRange(["--opt", $"build-arg:{name}={value}"]);
        }

        if (inputs.Text("target") is { Length: > 0 } target)
        {
            arguments.AddRange(["--opt", $"target={target}"]);
        }

        var references = References(image, inputs.TextList("tags"));
        arguments.AddRange(["--output", $"type=image,\"name={string.Join(',', references)}\",push={(push ? "true" : "false")}"]);
        return new BuildPlan(arguments, references, push, credentials);
    }

    private async Task<NodeResult> BuildAsync(NodeContext context, BuildPlan build, string number, Dictionary<string, string> env, string directory)
    {
        await services.Runtime.PullAsync(services.Options.BuildKitImage, line =>
        {
            context.Write(LogStream.System, line);
            return Task.CompletedTask;
        }, context.Stop).ConfigureAwait(false);
        var helper = await services.Runtime.StartAsync(
            new ContainerSpec(
                services.Options.BuildKitImage,
                ["-c", JobEnvironment.IdleScript],
                [.. paths.Binds, $"{services.Options.BuildCacheVolume}:{CacheDirectory}"],
                env,
                ContainerLabels.For(runnerId, jobId))
            {
                Entrypoint = ["sh"],
                SecurityOptions = ["seccomp=unconfined", "apparmor=unconfined"],
            },
            context.Stop).ConfigureAwait(false);
        try
        {
            context.Write(LogStream.System, $"Building {string.Join(", ", build.References)}{(build.Push ? " and pushing it" : string.Empty)}.");
            var exec = new ExecSpec(["buildctl-daemonless.sh", .. build.Arguments, "--metadata-file", $"{JobPaths.ContainerMeta}/build-{number}/metadata.json"], JobPaths.ContainerWorkspace, env);
            var running = services.Runtime.ExecAsync(helper, exec, line =>
            {
                context.Write(line.IsError ? LogStream.Stderr : LogStream.Stdout, line.Text);
                return Task.CompletedTask;
            }, CancellationToken.None);
            await Task.WhenAny(running, Task.Delay(Timeout.Infinite, context.Stop)).ConfigureAwait(false);
            context.Stop.ThrowIfCancellationRequested();
            var exitCode = await running.ConfigureAwait(false);
            if (exitCode != 0)
            {
                return NodeResult.Failed($"BuildKit exited with {exitCode}; the log above says why.");
            }

            var digest = await DigestAsync(Path.Combine(directory, "metadata.json"), context.Stop).ConfigureAwait(false);
            return new NodeResult(true, new Dictionary<string, string>(StringComparer.Ordinal) { ["digest"] = digest, ["image"] = build.References[0] }, null);
        }
        finally
        {
            // Removing the container also ends a build that was stopped.
            await services.Runtime.RemoveAsync(helper, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<string> DigestAsync(string metadata, CancellationToken cancellationToken)
    {
        if (!File.Exists(metadata))
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadata, cancellationToken).ConfigureAwait(false));
        return document.RootElement.TryGetProperty("containerimage.digest", out var digest) ? digest.GetString() ?? string.Empty : string.Empty;
    }

    private sealed record BuildPlan(List<string> Arguments, List<string> References, bool Push, string? Credentials);
}
