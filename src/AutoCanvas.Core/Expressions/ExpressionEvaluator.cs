namespace AutoCanvas.Core.Expressions;

/// <summary>
/// Evaluates expressions. Every value is text or <c>null</c>; comparisons compare text,
/// case-sensitively, and booleans are the texts <c>true</c> and <c>false</c>.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>Evaluates <paramref name="expression"/> to its value.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="resolver">Supplies the values paths read; unknown paths read as <c>null</c>.</param>
    /// <returns>The value as text, or <see langword="null"/>.</returns>
    public static string? Evaluate(Expression expression, IValueResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolver);

        return expression switch
        {
            LiteralExpression literal => literal.Value,
            PathExpression path => resolver.TryResolve(path.Segments, out var value) ? value : null,
            NotExpression not => Bool(!IsTrue(Evaluate(not.Operand, resolver))),
            BinaryExpression binary => EvaluateBinary(binary, resolver),
            _ => throw new ArgumentException($"Unknown expression type {expression.GetType().Name}.", nameof(expression)),
        };
    }

    /// <summary>Evaluates <paramref name="expression"/> as a condition.</summary>
    /// <param name="expression">The condition.</param>
    /// <param name="resolver">Supplies the values paths read.</param>
    /// <returns>Whether the value counts as true (see <see cref="IsTrue"/>).</returns>
    public static bool EvaluateCondition(Expression expression, IValueResolver resolver) => IsTrue(Evaluate(expression, resolver));

    /// <summary>
    /// Whether a value counts as true: everything except <c>null</c>, empty text, <c>false</c> and
    /// <c>0</c>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> unless the value is one of the false values.</returns>
    public static bool IsTrue(string? value) => value is not (null or "" or "false" or "0");

    private static string EvaluateBinary(BinaryExpression binary, IValueResolver resolver) => binary.Operator switch
    {
        BinaryOperator.Equal => Bool(string.Equals(Evaluate(binary.Left, resolver), Evaluate(binary.Right, resolver), StringComparison.Ordinal)),
        BinaryOperator.NotEqual => Bool(!string.Equals(Evaluate(binary.Left, resolver), Evaluate(binary.Right, resolver), StringComparison.Ordinal)),
        BinaryOperator.And => Bool(EvaluateCondition(binary.Left, resolver) && EvaluateCondition(binary.Right, resolver)),
        BinaryOperator.Or => Bool(EvaluateCondition(binary.Left, resolver) || EvaluateCondition(binary.Right, resolver)),
        _ => throw new ArgumentException($"Unknown operator {binary.Operator}.", nameof(binary)),
    };

    private static string Bool(bool value) => value ? "true" : "false";
}
