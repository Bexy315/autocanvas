namespace AutoCanvas.Core.Nodes;

/// <summary>What a node input takes. Any input may instead be an expression, checked when it runs.</summary>
public enum NodeInputKind
{
    /// <summary>A text; numbers and booleans are taken as their text.</summary>
    Text,

    /// <summary>A whole number.</summary>
    WholeNumber,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A list of texts.</summary>
    TextList,

    /// <summary>A mapping of names to texts.</summary>
    TextMap,
}
