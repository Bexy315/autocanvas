using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Tests.Conformance;

/// <summary>
/// Converts YAML to JSON for schema validation. Deliberately written separately from the parser's
/// own conversion, with the plain YAML 1.2 core-schema rules, so the two can catch each other out.
/// </summary>
internal static class YamlAsJson
{
    public static JsonElement Load(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return JsonSerializer.SerializeToElement(ToNode(stream.Documents[0].RootNode));
    }

    private static JsonNode? ToNode(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(e => KeyValuePair.Create(((YamlScalarNode)e.Key).Value!, ToNode(e.Value)))),
        YamlSequenceNode list => new JsonArray([.. list.Children.Select(ToNode)]),
        YamlScalarNode scalar => ToValue(scalar),
        _ => throw new NotSupportedException(node.GetType().Name),
    };

    private static JsonValue? ToValue(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? string.Empty;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(text);
        }

        if (text is "" or "~" or "null")
        {
            return null;
        }

        if (text is "true" or "false")
        {
            return JsonValue.Create(string.Equals(text, "true", StringComparison.Ordinal));
        }

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? JsonValue.Create(number)
            : JsonValue.Create(text);
    }
}
