using AutoCanvas.Core.Nodes;

namespace AutoCanvas.Server.Api.Nodes;

/// <summary>One key a node type takes under <c>with</c>.</summary>
/// <param name="Name">The key.</param>
/// <param name="Kind">What it takes; any input may instead be an expression.</param>
/// <param name="Required">Whether a step must give it.</param>
/// <param name="Description">What it means.</param>
/// <param name="Choices">The values a text input may have, when there are few; empty for any.</param>
internal sealed record NodeInputView(string Name, NodeInputKind Kind, bool Required, string Description, IReadOnlyList<string> Choices)
{
    public static NodeInputView From(NodeInput input) => new(input.Name, input.Kind, input.Required, input.Description, input.Choices);
}
