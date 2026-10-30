using System.Text.Json;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>
/// Reads automation files (format version 1) into an <see cref="AutomationDefinition"/>.
/// </summary>
/// <remarks>
/// The parser checks structure: types, required and unknown keys, allowed values and keys that
/// exclude each other — the same rules as <c>schema/automation.v1.json</c>. Whether the parts fit
/// together (needs naming existing jobs, no cycles, expressions reading what is reachable) is the
/// validator's job. The parser never throws for bad input; every problem becomes a
/// <see cref="Diagnostic"/>.
/// </remarks>
public static class AutomationParser
{
    /// <summary>Largest accepted file, in characters.</summary>
    public const int MaxLength = 1024 * 1024;

    /// <summary>Most YAML nodes a file may have once aliases are expanded.</summary>
    public const int MaxNodes = 100_000;

    /// <summary>Deepest nesting a file may have.</summary>
    public const int MaxDepth = 64;

    /// <summary>Parses the text of an automation file.</summary>
    /// <param name="yaml">The file's content.</param>
    /// <returns>The automation if the file has no errors, and all diagnostics.</returns>
    public static ParseResult Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var context = new ParseContext();
        var root = Load(yaml, context);
        var definition = root is null ? null : ReadDocument(root, context);
        return new ParseResult(context.HasErrors ? null : definition, context.Diagnostics);
    }

    private static YamlNode? Load(string yaml, ParseContext context)
    {
        if (yaml.Length > MaxLength)
        {
            context.Error(DiagnosticCodes.TooLarge, string.Empty, node: null, $"The file is larger than {MaxLength / 1024} KiB.");
            return null;
        }

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            var location = new SourceLocation((int)exception.Start.Line, (int)exception.Start.Column);
            context.Error(DiagnosticCodes.InvalidYaml, string.Empty, node: null, $"This is not valid YAML at {location}: {exception.Message}");
            return null;
        }

        if (stream.Documents.Count != 1)
        {
            context.Error(DiagnosticCodes.NotOneDocument, string.Empty, node: null, stream.Documents.Count == 0 ? "The file is empty." : "The file must contain exactly one YAML document.");
            return null;
        }

        var root = stream.Documents[0].RootNode;
        return CheckSize(root, context) ? root : null;
    }

    /// <summary>
    /// Counts nodes with aliases expanded, so that a file which is small on disk but huge once
    /// anchors are resolved ("billion laughs") is rejected before anything walks it in full.
    /// </summary>
    private static bool CheckSize(YamlNode root, ParseContext context)
    {
        var budget = MaxNodes;
        var pending = new Stack<(YamlNode Node, int Depth)>();
        pending.Push((root, 1));
        while (pending.TryPop(out var item))
        {
            if (--budget < 0 || item.Depth > MaxDepth)
            {
                context.Error(DiagnosticCodes.TooLarge, string.Empty, item.Node, $"The file is too large or too deeply nested (at most {MaxNodes} values and {MaxDepth} levels).");
                return false;
            }

            var children = item.Node switch
            {
                YamlMappingNode mapping => mapping.Children.SelectMany(pair => new[] { pair.Key, pair.Value }),
                YamlSequenceNode sequence => sequence.Children,
                _ => [],
            };
            foreach (var child in children)
            {
                pending.Push((child, item.Depth + 1));
            }
        }

        return true;
    }

    private static AutomationDefinition? ReadDocument(YamlNode root, ParseContext context)
    {
        if (Mapping.From(root, string.Empty, context, "the automation") is not { } map)
        {
            return null;
        }

        var version = ReadVersion(map, context);
        var definition = new AutomationDefinition(
            Version: version ?? AutomationSchemaVersion.Current,
            Name: ReadName(map.Take("name"), "name", context),
            Description: Values.Text(map.Take("description"), "description", context),
            Triggers: TriggersReader.Read(map.Take("triggers"), "triggers", context),
            Variables: Collections.VariableMap(map.Take("variables"), "variables", context) ?? Empty.Map,
            TimeoutMinutes: Values.Integer(map.Take("timeout_minutes"), "timeout_minutes", context, 1, JobReader.MaxTimeoutMinutes),
            Concurrency: ReadConcurrency(map.Take("concurrency"), context),
            ChangedWhen: Values.NonEmptyText(map.Take("changed_when"), "changed_when", context),
            Jobs: JobReader.ReadAll(map.Require("jobs", context), "jobs", context),
            Extensions: ReadExtensions(map));
        map.ReportUnknownKeys(context, allowExtensions: true);
        return definition;
    }

    private static int? ReadVersion(Mapping map, ParseContext context)
    {
        var node = map.Require("version", context);
        var version = Values.Integer(node, "version", context, int.MinValue, int.MaxValue);
        if (version is { } value && !AutomationSchemaVersion.IsSupported(value))
        {
            context.Error(DiagnosticCodes.UnsupportedVersion, "version", node, $"Version {value} is not supported; this AutoCanvas reads version {AutomationSchemaVersion.Current}.");
            return null;
        }

        return version;
    }

    private static string? ReadName(YamlNode? node, string path, ParseContext context)
    {
        var name = Values.NonEmptyText(node, path, context);
        if (name is { Length: > 100 })
        {
            context.Error(DiagnosticCodes.InvalidValue, path, node, "The name must be at most 100 characters long.");
            return null;
        }

        return name;
    }

    private static Concurrency ReadConcurrency(YamlNode? node, ParseContext context) =>
        Values.Choice(
            node,
            "concurrency",
            context,
            new Dictionary<string, Concurrency>(StringComparer.Ordinal) { ["parallel"] = Concurrency.Parallel, ["single"] = Concurrency.Exclusive })
        ?? Concurrency.Parallel;

    private static Dictionary<string, JsonElement> ReadExtensions(Mapping map)
    {
        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, _, value) in map.Entries)
        {
            if (key.StartsWith("x-", StringComparison.Ordinal))
            {
                extensions[key] = JsonValues.ToElement(value);
            }
        }

        return extensions;
    }
}
