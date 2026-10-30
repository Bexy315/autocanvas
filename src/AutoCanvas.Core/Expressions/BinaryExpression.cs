namespace AutoCanvas.Core.Expressions;

/// <summary>A comparison or logical combination of two expressions.</summary>
/// <param name="Operator">The operator.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
public sealed record BinaryExpression(BinaryOperator Operator, Expression Left, Expression Right) : Expression;
