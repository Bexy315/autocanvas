using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>
/// Converts free-form YAML (node inputs, extension data) to JSON, typing scalars by the YAML 1.2
/// core schema.
/// </summary>
internal static class JsonValues
{
    public static JsonElement ToElement(YamlNode? node) =>
        node is null ? EmptyObject() : JsonSerializer.SerializeToElement(ToNode(node), JsonNodeContext.Default.JsonNode);

    public static JsonElement EmptyObject() => JsonSerializer.SerializeToElement(new JsonObject(), JsonNodeContext.Default.JsonNode);

    private static JsonNode? ToNode(YamlNode node) => node switch
    {
        YamlMappingNode mapping => ToObject(mapping),
        YamlSequenceNode sequence => new JsonArray([.. sequence.Children.Select(ToNode)]),
        YamlScalarNode scalar => ToValue(scalar),
        _ => null,
    };

    private static JsonObject ToObject(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var (key, value) in mapping.Children)
        {
            // Later duplicates cannot occur: YamlDotNet rejects duplicate keys while loading.
            result[key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : key.ToString()] = ToNode(value);
        }

        return result;
    }

    private static JsonValue? ToValue(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? string.Empty;
        return Scalars.KindOf(scalar) switch
        {
            ScalarKind.Null => null,
            ScalarKind.Boolean => JsonValue.Create(Scalars.IsTrue(text)),
            ScalarKind.Integer when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) => JsonValue.Create(number),
            ScalarKind.Float when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) => JsonValue.Create(number),
            _ => JsonValue.Create(text),
        };
    }
}
