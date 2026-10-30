namespace AutoCanvas.Core.Expressions;

/// <summary>Kinds of tokens of the expression language.</summary>
internal enum TokenKind
{
    End,
    Path,
    String,
    Number,
    True,
    False,
    Null,
    Equal,
    NotEqual,
    And,
    Or,
    Not,
    OpenParenthesis,
    CloseParenthesis,
}
