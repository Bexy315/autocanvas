namespace AutoCanvas.Server.Application.Runs;

/// <summary>A resolved node step.</summary>
/// <param name="Type">The node type.</param>
/// <param name="Inputs">Its inputs as a JSON object, every expression rendered.</param>
public sealed record ResolvedNode(string Type, string Inputs);
