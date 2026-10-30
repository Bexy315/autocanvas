using AutoCanvas.Core.Diagnostics;

namespace AutoCanvas.Core.Validation;

/// <summary>Collects the validator's diagnostics and attaches source positions when known.</summary>
internal sealed class ValidationContext(IReadOnlyDictionary<string, SourceLocation> locations)
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public void Error(string code, string path, string message) =>
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, message, path, Locate(path)));

    /// <summary>The position of <paramref name="path"/>, or of its closest parent that has one.</summary>
    private SourceLocation? Locate(string path)
    {
        var current = path;
        while (true)
        {
            if (locations.TryGetValue(current, out var location))
            {
                return location;
            }

            var cut = Math.Max(current.LastIndexOf('.'), current.LastIndexOf('['));
            if (cut <= 0)
            {
                return null;
            }

            current = current[..cut];
        }
    }
}
