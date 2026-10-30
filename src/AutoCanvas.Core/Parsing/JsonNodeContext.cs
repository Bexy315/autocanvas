using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AutoCanvas.Core.Parsing;

/// <summary>Source-generated serialization for <see cref="JsonNode"/>, so Core needs no reflection-based JSON.</summary>
[JsonSerializable(typeof(JsonNode))]
internal sealed partial class JsonNodeContext : JsonSerializerContext;
