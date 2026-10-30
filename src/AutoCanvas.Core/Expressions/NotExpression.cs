namespace AutoCanvas.Core.Expressions;

/// <summary>Logical negation, <c>!operand</c>.</summary>
/// <param name="Operand">The negated expression.</param>
public sealed record NotExpression(Expression Operand) : Expression;
