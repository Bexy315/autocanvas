using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>
/// Resolves the type of scalars by the YAML 1.2 core schema, so that <c>on</c>, <c>yes</c> or
/// <c>no</c> stay text (YAML 1.1 would read them as booleans) and only unquoted scalars can be
/// anything but text.
/// </summary>
internal static partial class Scalars
{
    public static ScalarKind KindOf(YamlScalarNode node)
    {
        if (node.Style != ScalarStyle.Plain)
        {
            return ScalarKind.String;
        }

        var text = node.Value ?? string.Empty;
        if (text.Length == 0 || text is "~" or "null" or "Null" or "NULL")
        {
            return ScalarKind.Null;
        }

        if (text is "true" or "True" or "TRUE" or "false" or "False" or "FALSE")
        {
            return ScalarKind.Boolean;
        }

        if (IntegerPattern().IsMatch(text))
        {
            return ScalarKind.Integer;
        }

        return FloatPattern().IsMatch(text) ? ScalarKind.Float : ScalarKind.String;
    }

    public static bool IsTrue(string text) => text is "true" or "True" or "TRUE";

    public static string Describe(ScalarKind kind) => kind switch
    {
        ScalarKind.Null => "an empty value",
        ScalarKind.Boolean => "true/false",
        ScalarKind.Integer or ScalarKind.Float => "a number",
        _ => "text",
    };

    [GeneratedRegex("^(?:[-+]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"^(?:[-+]?(?:\.[0-9]+|[0-9]+(?:\.[0-9]*)?)(?:[eE][-+]?[0-9]+)?|[-+]?\.(?:inf|Inf|INF)|\.(?:nan|NaN|NAN))$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FloatPattern();
}
