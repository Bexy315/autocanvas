using System.Text.Json;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// Reading fields of forges' payloads, which come from outside: a field of another kind than
/// expected is simply not there, where <see cref="JsonElement"/> itself would throw.
/// </summary>
internal static class JsonFields
{
    /// <summary>The object in a field of an object.</summary>
    public static bool Object(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;
    }

    /// <summary>The whole number in a field of an object.</summary>
    public static bool Int64(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out value);
    }

    /// <summary>The whole number in a field of an object, if it fits an <see cref="int"/>.</summary>
    public static bool Int32(JsonElement element, string name, out int value)
    {
        value = 0;
        return Int64(element, name, out var wide) && wide is >= int.MinValue and <= int.MaxValue && (value = (int)wide) == wide;
    }
}
