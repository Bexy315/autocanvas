namespace AutoCanvas.Core.Parsing;

/// <summary>Shared empty values for absent optional keys.</summary>
internal static class Empty
{
    public static IReadOnlyDictionary<string, string> Map { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
