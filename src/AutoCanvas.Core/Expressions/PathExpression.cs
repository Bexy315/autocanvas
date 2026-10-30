namespace AutoCanvas.Core.Expressions;

/// <summary>A value read from the run, such as <c>jobs.build.outputs.digest</c>.</summary>
/// <param name="Segments">The dot-separated names, in order.</param>
/// <param name="Offset">Position of the path in the expression text, for messages.</param>
public sealed record PathExpression(IReadOnlyList<string> Segments, int Offset) : Expression
{
    /// <summary>The path as written, for example <c>jobs.build.result</c>.</summary>
    public string Text => string.Join('.', Segments);
}
