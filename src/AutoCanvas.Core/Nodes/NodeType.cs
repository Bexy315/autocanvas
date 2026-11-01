namespace AutoCanvas.Core.Nodes;

/// <summary>A node type a step can use (<c>type</c>), with the inputs it takes and the outputs it sets.</summary>
/// <param name="Name">The type, such as <c>http_request</c>.</param>
/// <param name="Description">What it does.</param>
/// <param name="Inputs">Its inputs.</param>
/// <param name="Outputs">The outputs a step of this type sets, by name with a description.</param>
public sealed record NodeType(string Name, string Description, IReadOnlyList<NodeInput> Inputs, IReadOnlyDictionary<string, string> Outputs);
