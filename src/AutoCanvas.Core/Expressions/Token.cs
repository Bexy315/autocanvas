namespace AutoCanvas.Core.Expressions;

/// <summary>A token of the expression language.</summary>
internal readonly record struct Token(TokenKind Kind, string Text, int Offset);
