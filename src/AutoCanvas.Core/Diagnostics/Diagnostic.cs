namespace AutoCanvas.Core.Diagnostics;

/// <summary>
/// A problem found in an automation, phrased for the person who wrote it.
/// </summary>
/// <param name="Severity">Whether the automation can still be used.</param>
/// <param name="Code">Stable identifier of the kind of problem, for example <c>AC0103</c>; see <see cref="DiagnosticCodes"/>.</param>
/// <param name="Message">What is wrong and how to fix it.</param>
/// <param name="Path">Where in the document, as a dotted path such as <c>jobs.build.steps[0].run</c>; empty for the whole document.</param>
/// <param name="Location">Line and column in the file, when the problem can be pinned to one.</param>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string Path,
    SourceLocation? Location)
{
    /// <inheritdoc/>
    public override string ToString()
    {
        var where = Location is { } location ? $"({location}) " : string.Empty;
        var path = Path.Length > 0 ? $"{Path}: " : string.Empty;
        return $"{where}{Code} {path}{Message}";
    }
}
