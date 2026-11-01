namespace AutoCanvas.Core.Nodes;

/// <summary>One input of a node type (a key under <c>with</c>).</summary>
/// <param name="Name">The key.</param>
/// <param name="Kind">What it takes.</param>
/// <param name="Required">Whether a step must give it.</param>
/// <param name="Description">What it means, for documentation and editors.</param>
/// <param name="Choices">The values a text input may have, when there are few; empty for any.</param>
public sealed record NodeInput(string Name, NodeInputKind Kind, bool Required, string Description, IReadOnlyList<string> Choices)
{
    /// <summary>An input without a fixed set of values.</summary>
    public NodeInput(string name, NodeInputKind kind, bool required, string description)
        : this(name, kind, required, description, [])
    {
    }
}
