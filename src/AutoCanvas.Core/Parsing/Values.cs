using System.Globalization;
using System.Text.RegularExpressions;
using AutoCanvas.Core.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Reads typed values from YAML nodes and reports values of the wrong type or form.</summary>
internal static partial class Values
{
    public static string? Text(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return null;
        }

        if (node is YamlScalarNode scalar)
        {
            var kind = Scalars.KindOf(scalar);
            if (kind == ScalarKind.String)
            {
                return scalar.Value!;
            }

            context.Error(DiagnosticCodes.WrongType, path, node, $"Expected text but found {Scalars.Describe(kind)}. Put the value in quotes if it is meant as text.");
            return null;
        }

        context.Error(DiagnosticCodes.WrongType, path, node, "Expected text but found a list or mapping.");
        return null;
    }

    /// <summary>A non-empty text value.</summary>
    public static string? NonEmptyText(YamlNode? node, string path, ParseContext context)
    {
        var text = Text(node, path, context);
        if (text is { Length: 0 })
        {
            context.Error(DiagnosticCodes.InvalidValue, path, node, "This must not be empty.");
            return null;
        }

        return text;
    }

    /// <summary>A scalar of any type as written: text, number or boolean, but not empty.</summary>
    public static string? ScalarText(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return null;
        }

        if (node is YamlScalarNode scalar && Scalars.KindOf(scalar) != ScalarKind.Null)
        {
            return scalar.Value!;
        }

        context.Error(DiagnosticCodes.WrongType, path, node, "Expected text, a number or true/false.");
        return null;
    }

    public static bool? Boolean(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return null;
        }

        if (node is YamlScalarNode scalar && Scalars.KindOf(scalar) == ScalarKind.Boolean)
        {
            return Scalars.IsTrue(scalar.Value!);
        }

        context.Error(DiagnosticCodes.WrongType, path, node, "Expected true or false.");
        return null;
    }

    public static int? Integer(YamlNode? node, string path, ParseContext context, int minimum, int maximum)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not YamlScalarNode scalar
            || Scalars.KindOf(scalar) != ScalarKind.Integer
            || !int.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            context.Error(DiagnosticCodes.WrongType, path, node, "Expected a whole number.");
            return null;
        }

        if (value < minimum || value > maximum)
        {
            context.Error(DiagnosticCodes.InvalidValue, path, node, $"Must be between {minimum} and {maximum}.");
            return null;
        }

        return value;
    }

    /// <summary>An id usable in expressions: lower-case letters, digits, '_' and '-', starting with a letter.</summary>
    public static string? Identifier(YamlNode? node, string path, ParseContext context)
    {
        var text = Text(node, path, context);
        if (text is null)
        {
            return null;
        }

        return CheckIdentifier(text, path, node, context) ? text : null;
    }

    public static bool CheckIdentifier(string text, string path, YamlNode? node, ParseContext context)
    {
        if (IdentifierPattern().IsMatch(text))
        {
            return true;
        }

        context.Error(
            DiagnosticCodes.InvalidValue,
            path,
            node,
            $"'{text}' is not a valid id. Use lower-case letters, digits, '_' and '-', starting with a letter (at most 63 characters).");
        return false;
    }

    /// <summary>One of <paramref name="allowed"/>, by its exact spelling in the file.</summary>
    public static T? Choice<T>(YamlNode? node, string path, ParseContext context, IReadOnlyDictionary<string, T> allowed)
        where T : struct
    {
        var text = Text(node, path, context);
        if (text is null)
        {
            return null;
        }

        if (allowed.TryGetValue(text, out var value))
        {
            return value;
        }

        context.Error(DiagnosticCodes.InvalidValue, path, node, $"'{text}' is not allowed here. Use one of: {string.Join(", ", allowed.Keys)}.");
        return null;
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]{0,62}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdentifierPattern();
}
