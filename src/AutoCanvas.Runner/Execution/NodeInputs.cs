using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// A node step's inputs as the server resolved them. The server checked their kinds when the
/// automation was saved; values that came from expressions arrive as text and are read here, and a
/// value that does not fit is the step's mistake (<see cref="NodeInputException"/>).
/// </summary>
internal sealed class NodeInputs(JsonElement inputs)
{
    public static NodeInputs From(Struct? inputs) =>
        new(inputs is null ? JsonDocument.Parse("{}").RootElement : JsonDocument.Parse(JsonFormatter.Default.Format(inputs)).RootElement.Clone());

    /// <summary>A text input, or <see langword="null"/> when the step does not give it.</summary>
    public string? Text(string name) => Get(name) is { } value ? Scalar(name, value) : null;

    public string RequiredText(string name) =>
        Text(name) is { Length: > 0 } text ? text : throw new NodeInputException($"The input '{name}' must be set.");

    public bool Boolean(string name, bool fallback) => Get(name) switch
    {
        null => fallback,
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        { ValueKind: JsonValueKind.String } text when bool.TryParse(text.GetString(), out var parsed) => parsed,
        _ => throw new NodeInputException($"The input '{name}' must be true or false."),
    };

    public int WholeNumber(string name, int fallback, int min, int max)
    {
        var text = Text(name);
        if (text is null)
        {
            return fallback;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == Math.Floor(number) && number >= min && number <= max
            ? (int)number
            : throw new NodeInputException($"The input '{name}' must be a whole number from {min} to {max}.");
    }

    /// <summary>A list input; a single text (such as an expression's result) is a list of one.</summary>
    public List<string> TextList(string name) => Get(name) switch
    {
        null => [],
        { ValueKind: JsonValueKind.Array } array => [.. array.EnumerateArray().Select(item => Scalar(name, item))],
        { } single => [Scalar(name, single)],
    };

    public Dictionary<string, string> TextMap(string name) => Get(name) switch
    {
        null => new(StringComparer.Ordinal),
        { ValueKind: JsonValueKind.Object } map => map.EnumerateObject().ToDictionary(p => p.Name, p => Scalar($"{name}.{p.Name}", p.Value), StringComparer.Ordinal),
        _ => throw new NodeInputException($"The input '{name}' must be a mapping of names to texts."),
    };

    private static string Scalar(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.GetDouble().ToString(CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => throw new NodeInputException($"The input '{name}' must be a text."),
    };

    private JsonElement? Get(string name) =>
        inputs.ValueKind == JsonValueKind.Object && inputs.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
}
