using System.Text.Json;
using System.Text.Json.Serialization;
using AutoCanvas.Persistence.Runners;
using AutoCanvas.Server.Application.Common;

namespace AutoCanvas.Server.Application.Runners;

/// <summary>
/// What a runner announced it can do, at registration and again on every connect. The server
/// offers it only jobs whose labels, node types and executor it has.
/// </summary>
/// <param name="Labels">Labels matched against a job's <c>runs_on</c>.</param>
/// <param name="Executor">How the runner executes steps.</param>
/// <param name="Os">Operating system, such as <c>linux</c>.</param>
/// <param name="Arch">CPU architecture, such as <c>amd64</c>.</param>
/// <param name="NodeTypes">Node types the runner implements.</param>
/// <param name="MaxConcurrentJobs">How many jobs it runs at the same time.</param>
/// <param name="RunnerVersion">Implementation and version, such as <c>autocanvas-runner-dotnet/0.1.0</c>.</param>
/// <param name="Shells">Shells available for script steps.</param>
public sealed record RunnerCapabilities(
    IReadOnlyList<string> Labels,
    RunnerExecutor Executor,
    string Os,
    string Arch,
    IReadOnlyList<string> NodeTypes,
    int MaxConcurrentJobs,
    string RunnerVersion,
    IReadOnlyList<string> Shells)
{
    /// <summary>The most labels, node types or shells a runner can announce.</summary>
    public const int MaxNames = 32;

    /// <summary>The longest label, node type, shell, operating system, architecture or version.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The most jobs a runner can run at the same time.</summary>
    public const int MaxJobs = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Whether the runner can run jobs that need a container (an image or services).</summary>
    [JsonIgnore]
    public bool RunsContainers => Executor == RunnerExecutor.Container;

    /// <summary>An error for announcements that make no sense or would not fit; <see langword="null"/> if valid.</summary>
    public UseCaseError? Check()
    {
        if (MaxConcurrentJobs is < 1 or > MaxJobs)
        {
            return UseCaseError.Invalid("capabilities.max_concurrent_jobs", $"max_concurrent_jobs must be between 1 and {MaxJobs}.");
        }

        foreach (var (field, names) in new[] { ("labels", Labels), ("node_types", NodeTypes), ("shells", Shells) })
        {
            if (names.Count > MaxNames || names.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > MaxNameLength || n.Any(char.IsControl)))
            {
                return UseCaseError.Invalid($"capabilities.{field}", $"{field} must be at most {MaxNames} names of 1 to {MaxNameLength} characters.");
            }
        }

        foreach (var (field, value) in new[] { ("os", Os), ("arch", Arch), ("runner_version", RunnerVersion) })
        {
            if (value.Length > MaxNameLength || value.Any(char.IsControl))
            {
                return UseCaseError.Invalid($"capabilities.{field}", $"{field} must have at most {MaxNameLength} characters.");
            }
        }

        return null;
    }

    /// <summary>The form stored in <see cref="Runner.Capabilities"/>.</summary>
    public string Write() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads what <see cref="Write"/> stored.</summary>
    public static RunnerCapabilities Read(string json) =>
        JsonSerializer.Deserialize<RunnerCapabilities>(json, Json) ?? throw new InvalidOperationException("Stored runner capabilities are empty.");
}
