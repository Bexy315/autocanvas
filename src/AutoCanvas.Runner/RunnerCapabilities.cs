using System.Reflection;
using System.Runtime.InteropServices;
using AutoCanvas.Protocol.Runner.V1;

namespace AutoCanvas.Runner;

/// <summary>What this runner announces in <c>Register</c> and <c>Hello</c>.</summary>
internal static class RunnerCapabilities
{
    /// <summary>The protocol version the runner speaks.</summary>
    public const string ProtocolVersion = "1.0";

    /// <summary>
    /// The shells steps may ask for. The runner checks that the job's image has the shell before a
    /// step uses it, and reports a missing one as an error of the runner, not of the script.
    /// </summary>
    public static readonly IReadOnlyList<string> Shells = ["bash", "sh"];

    /// <summary>The runner's version, for the server's runner list.</summary>
    public static string Version { get; } =
        typeof(RunnerCapabilities).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static Capabilities Of(RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var capabilities = new Capabilities
        {
            Executor = Executor.Container,
            Os = "linux",
            Arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "amd64",
                Architecture.X86 => "386",
                Architecture.Arm => "arm",
                _ => "unknown",
            },
            MaxConcurrentJobs = (uint)options.MaxConcurrentJobs,
            RunnerVersion = $"autocanvas-runner/{Version}",
        };
        capabilities.Labels.Add(options.EffectiveLabels);
        capabilities.Shells.Add(Shells);
        return capabilities;
    }
}
