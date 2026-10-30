namespace AutoCanvas.Core.Expressions;

/// <summary>A <c>${{ … }}</c> in a template, replaced by its value.</summary>
/// <param name="Expression">The parsed expression.</param>
/// <param name="Offset">Position of the <c>${{</c> in the template text.</param>
public sealed record ExpressionPart(Expression Expression, int Offset) : TemplatePart;
