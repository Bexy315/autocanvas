using AutoCanvas.Core.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>
/// A YAML mapping read key by key. Keys that were never taken are reported as unknown, so a typo
/// such as <c>need:</c> for <c>needs:</c> is an error instead of being silently ignored.
/// </summary>
internal sealed class Mapping
{
    private readonly List<(string Key, YamlNode KeyNode, YamlNode Value)> _entries = [];
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    private Mapping(YamlMappingNode node, string path)
    {
        Node = node;
        Path = path;
        foreach (var (key, value) in node.Children)
        {
            _entries.Add((key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : string.Empty, key, value));
        }
    }

    public YamlMappingNode Node { get; }

    public string Path { get; }

    public IEnumerable<(string Key, YamlNode KeyNode, YamlNode Value)> Entries => _entries;

    /// <summary>Reads <paramref name="node"/> as a mapping, or reports what it is instead.</summary>
    public static Mapping? From(YamlNode node, string path, ParseContext context, string what)
    {
        if (node is YamlMappingNode mapping)
        {
            return new Mapping(mapping, path);
        }

        context.Error(DiagnosticCodes.WrongType, path, node, $"Expected {what} as a mapping of keys and values.");
        return null;
    }

    public bool Has(string key) => _entries.Exists(e => string.Equals(e.Key, key, StringComparison.Ordinal));

    /// <summary>The value of <paramref name="key"/>, marking the key as known; <see langword="null"/> when absent.</summary>
    public YamlNode? Take(string key)
    {
        _taken.Add(key);
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>Like <see cref="Take"/>, but reports a missing key.</summary>
    public YamlNode? Require(string key, ParseContext context)
    {
        var value = Take(key);
        if (value is null)
        {
            context.Error(DiagnosticCodes.MissingKey, Path, Node, $"'{key}' is required here.");
        }

        return value;
    }

    /// <summary>Reports every key that was not taken. <c>x-…</c> keys are allowed when <paramref name="allowExtensions"/> is set.</summary>
    public void ReportUnknownKeys(ParseContext context, bool allowExtensions = false)
    {
        foreach (var (key, keyNode, _) in _entries)
        {
            if (_taken.Contains(key) || (allowExtensions && key.StartsWith("x-", StringComparison.Ordinal)))
            {
                continue;
            }

            context.Error(
                DiagnosticCodes.UnknownKey,
                ParseContext.Child(Path, key),
                keyNode,
                $"'{key}' is not a known key here.{Suggestions.ForKey(key, _taken)}");
        }
    }
}
