namespace AutoCanvas.Core.Expressions;

/// <summary>A literal: text, a number, <c>true</c>, <c>false</c> or <c>null</c>.</summary>
/// <param name="Value">The value as text (<c>true</c>, <c>42</c>, …), or <see langword="null"/> for <c>null</c>.</param>
public sealed record LiteralExpression(string? Value) : Expression;
