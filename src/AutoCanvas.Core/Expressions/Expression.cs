namespace AutoCanvas.Core.Expressions;

/// <summary>
/// A parsed expression: the part between <c>${{</c> and <c>}}</c>, or a condition such as an
/// <c>if</c>. Evaluate it with <see cref="ExpressionEvaluator"/>.
/// </summary>
public abstract record Expression;
