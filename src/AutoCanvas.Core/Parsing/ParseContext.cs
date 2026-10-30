using AutoCanvas.Core.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Collects diagnostics while one document is read.</summary>
internal sealed class ParseContext
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public bool HasErrors { get; private set; }

    public void Error(string code, string path, YamlNode? node, string message)
    {
        HasErrors = true;
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, message, path, LocationOf(node)));
    }

    public static SourceLocation? LocationOf(YamlNode? node) =>
        node is null ? null : new SourceLocation((int)node.Start.Line, (int)node.Start.Column);

    public static string Child(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    public static string Item(string path, int index) => $"{path}[{index}]";
}
