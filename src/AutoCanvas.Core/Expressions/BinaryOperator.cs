namespace AutoCanvas.Core.Expressions;

/// <summary>Operators between two expressions.</summary>
public enum BinaryOperator
{
    /// <summary><c>==</c>: equal as text, case-sensitive.</summary>
    Equal,

    /// <summary><c>!=</c>: not equal as text.</summary>
    NotEqual,

    /// <summary><c>&amp;&amp;</c>: both are true; the right side is only evaluated when the left one is true.</summary>
    And,

    /// <summary><c>||</c>: at least one is true; the right side is only evaluated when the left one is false.</summary>
    Or,
}
