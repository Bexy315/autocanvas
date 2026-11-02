using AutoCanvas.Core.Nodes;

namespace AutoCanvas.Server.Api.Nodes;

/// <summary>A node type a step can use with <c>type:</c>, for editors and documentation.</summary>
/// <param name="Name">The value of <c>type</c>, such as <c>http_request</c>.</param>
/// <param name="Description">What it does.</param>
/// <param name="Inputs">The keys it takes under <c>with</c>, required ones first.</param>
/// <param name="Outputs">The outputs a step of this type sets, by name.</param>
internal sealed record NodeTypeView(string Name, string Description, IReadOnlyList<NodeInputView> Inputs, IReadOnlyList<NodeOutputView> Outputs)
{
    public static NodeTypeView From(NodeType type) => new(
        type.Name,
        type.Description,
        [.. type.Inputs.OrderByDescending(i => i.Required).Select(NodeInputView.From)],
        [.. type.Outputs.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => new NodeOutputView(o.Key, o.Value))]);
}
