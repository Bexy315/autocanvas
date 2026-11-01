namespace AutoCanvas.Runner.Execution;

/// <summary>How a node step ended.</summary>
/// <param name="Succeeded">Whether it did what it should.</param>
/// <param name="Outputs">Its outputs.</param>
/// <param name="Error">Why it failed.</param>
internal sealed record NodeResult(bool Succeeded, IReadOnlyDictionary<string, string> Outputs, string? Error)
{
    public static NodeResult Failed(string error, IReadOnlyDictionary<string, string>? outputs = null) =>
        new(false, outputs ?? new Dictionary<string, string>(StringComparer.Ordinal), error);
}
