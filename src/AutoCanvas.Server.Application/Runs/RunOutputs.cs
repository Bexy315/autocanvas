using System.Text.Json;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>Outputs of steps and jobs as they are stored: a JSON object of strings.</summary>
internal static class RunOutputs
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Read(string? json) =>
        json is null ? Empty : JsonSerializer.Deserialize<Dictionary<string, string>>(json) is { } read
            ? new Dictionary<string, string>(read, StringComparer.Ordinal)
            : Empty;

    public static string Write(IReadOnlyDictionary<string, string> outputs) => JsonSerializer.Serialize(outputs);
}
