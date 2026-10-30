using System.Text.RegularExpressions;
using AutoCanvas.Core.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Reads lists and maps of simple values.</summary>
internal static partial class Collections
{
    /// <summary>A list of non-empty, distinct texts; at least one.</summary>
    public static IReadOnlyList<string>? TextList(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not YamlSequenceNode sequence)
        {
            context.Error(DiagnosticCodes.WrongType, path, node, "Expected a list.");
            return null;
        }

        return ReadTexts(sequence, path, context);
    }

    /// <summary>One text or a list of texts.</summary>
    public static IReadOnlyList<string>? TextOrList(YamlNode? node, string path, ParseContext context)
    {
        if (node is YamlScalarNode)
        {
            var text = Values.NonEmptyText(node, path, context);
            return text is null ? null : [text];
        }

        return TextList(node, path, context);
    }

    /// <summary>Environment-style variables: names like <c>NAME_1</c>, values text, numbers or booleans as written.</summary>
    public static IReadOnlyDictionary<string, string>? VariableMap(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return null;
        }

        var mapping = Mapping.From(node, path, context, "variables");
        if (mapping is null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, keyNode, value) in mapping.Entries)
        {
            var itemPath = ParseContext.Child(path, key);
            if (!VariableNamePattern().IsMatch(key))
            {
                context.Error(DiagnosticCodes.InvalidValue, itemPath, keyNode, $"'{key}' is not a valid variable name. Use letters, digits and '_', not starting with a digit.");
                continue;
            }

            if (Values.ScalarText(value, itemPath, context) is { } text)
            {
                result[key] = text;
            }
        }

        return result;
    }

    /// <summary>Outputs: ids mapped to expressions.</summary>
    public static IReadOnlyDictionary<string, string>? OutputMap(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return null;
        }

        var mapping = Mapping.From(node, path, context, "outputs");
        if (mapping is null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, keyNode, value) in mapping.Entries)
        {
            var itemPath = ParseContext.Child(path, key);
            if (Values.CheckIdentifier(key, itemPath, keyNode, context) && Values.Text(value, itemPath, context) is { } text)
            {
                result[key] = text;
            }
        }

        return result;
    }

    private static List<string>? ReadTexts(YamlSequenceNode sequence, string path, ParseContext context)
    {
        if (sequence.Children.Count == 0)
        {
            context.Error(DiagnosticCodes.InvalidValue, path, sequence, "The list must not be empty; leave the key out instead.");
            return null;
        }

        var result = new List<string>();
        for (var i = 0; i < sequence.Children.Count; i++)
        {
            var itemPath = ParseContext.Item(path, i);
            var text = Values.NonEmptyText(sequence.Children[i], itemPath, context);
            if (text is null)
            {
                continue;
            }

            if (result.Contains(text, StringComparer.Ordinal))
            {
                context.Error(DiagnosticCodes.InvalidValue, itemPath, sequence.Children[i], $"'{text}' is listed twice.");
                continue;
            }

            result.Add(text);
        }

        return result;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VariableNamePattern();
}
