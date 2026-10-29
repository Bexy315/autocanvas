namespace AutoCanvas.Reports;

/// <summary>
/// Maps the format names used in pipeline definitions to <see cref="ReportFormat"/>.
/// </summary>
public static class ReportFormats
{
    private static readonly Dictionary<string, ReportFormat> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["junit"] = ReportFormat.JUnit,
        ["trx"] = ReportFormat.Trx,
        ["cobertura"] = ReportFormat.Cobertura,
    };

    /// <summary>The format names accepted in pipeline definitions, in lower case.</summary>
    public static IReadOnlyCollection<string> Names => ByName.Keys;

    /// <summary>
    /// Resolves a format name from a pipeline definition, ignoring case.
    /// </summary>
    /// <param name="name">The format name, for example <c>junit</c>.</param>
    /// <param name="format">The resolved format if the name is known.</param>
    /// <returns><see langword="true"/> if <paramref name="name"/> names a supported format.</returns>
    public static bool TryParse(string? name, out ReportFormat format)
    {
        if (name is null)
        {
            format = default;
            return false;
        }

        return ByName.TryGetValue(name.Trim(), out format);
    }
}
