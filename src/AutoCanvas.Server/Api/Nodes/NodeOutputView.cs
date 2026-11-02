namespace AutoCanvas.Server.Api.Nodes;

/// <summary>An output a step of a node type sets, readable as <c>steps.ID.outputs.NAME</c>.</summary>
/// <param name="Name">The output's name.</param>
/// <param name="Description">What it holds.</param>
internal sealed record NodeOutputView(string Name, string Description);
