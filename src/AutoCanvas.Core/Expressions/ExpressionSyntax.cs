namespace AutoCanvas.Core.Expressions;

/// <summary>
/// Parses the expression language: paths (<c>vars.NAME</c>), literals (<c>'text'</c>, numbers,
/// <c>true</c>, <c>false</c>, <c>null</c>), <c>==</c>, <c>!=</c>, <c>&amp;&amp;</c>, <c>||</c>,
/// <c>!</c> and parentheses. Precedence from low to high: <c>||</c>, <c>&amp;&amp;</c>,
/// <c>==</c>/<c>!=</c>, <c>!</c>.
/// </summary>
public static class ExpressionSyntax
{
    /// <summary>Longest expression accepted, in characters.</summary>
    public const int MaxLength = 4096;

    /// <summary>Deepest nesting of parentheses and operators accepted.</summary>
    public const int MaxDepth = 32;

    /// <summary>Parses one expression, the text between <c>${{</c> and <c>}}</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <returns>The expression, or the first syntax error.</returns>
    public static (Expression? Expression, ExpressionError? Error) Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxLength)
        {
            return (null, new ExpressionError($"The expression is longer than {MaxLength} characters.", 0));
        }

        var (tokens, error) = Tokenizer.Tokenize(text);
        if (error is not null)
        {
            return (null, error);
        }

        if (tokens[0].Kind == TokenKind.End)
        {
            return (null, new ExpressionError("The expression is empty.", 0));
        }

        return new Parser(tokens).ParseAll();
    }

    /// <summary>
    /// Parses a condition (<c>if</c>, <c>changed_when</c>): an expression, optionally wrapped in a
    /// single <c>${{ … }}</c>.
    /// </summary>
    /// <param name="text">The condition as written.</param>
    /// <returns>The expression, or the first syntax error with its offset in <paramref name="text"/>.</returns>
    public static (Expression? Expression, ExpressionError? Error) ParseCondition(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        var start = text.IndexOf(trimmed, StringComparison.Ordinal);
        if (trimmed.StartsWith("${{", StringComparison.Ordinal) && trimmed.EndsWith("}}", StringComparison.Ordinal)
            && trimmed.IndexOf("}}", StringComparison.Ordinal) == trimmed.Length - 2)
        {
            var (expression, error) = Parse(trimmed[3..^2]);
            return (expression, error is null ? null : error with { Offset = error.Offset + start + 3 });
        }

        if (text.Contains("${{", StringComparison.Ordinal))
        {
            return (null, new ExpressionError("A condition is one expression: write it either without ${{ }} or wrapped in a single ${{ … }}.", text.IndexOf("${{", StringComparison.Ordinal)));
        }

        return Parse(text);
    }

    /// <summary>
    /// Recursive descent over the token list. The first error is remembered and every later step
    /// returns a placeholder, so parsing unwinds without exceptions.
    /// </summary>
    private sealed class Parser(List<Token> tokens)
    {
        private static readonly LiteralExpression Placeholder = new(Value: null);

        private int _position;
        private int _depth;
        private ExpressionError? _error;

        private Token Current => tokens[_position];

        public (Expression? Expression, ExpressionError? Error) ParseAll()
        {
            var expression = ParseOr();
            if (_error is null && Current.Kind != TokenKind.End)
            {
                Fail($"Unexpected '{Current.Text}'; an operator such as == or && is missing.", Current.Offset);
            }

            return _error is null ? (expression, null) : (null, _error);
        }

        private LiteralExpression Fail(string message, int offset)
        {
            _error ??= new ExpressionError(message, offset);
            return Placeholder;
        }

        private Expression ParseOr() => ParseBinary(TokenKind.Or, BinaryOperator.Or, ParseAnd);

        private Expression ParseAnd() => ParseBinary(TokenKind.And, BinaryOperator.And, ParseComparison);

        private Expression ParseBinary(TokenKind kind, BinaryOperator @operator, Func<Expression> operand)
        {
            var left = operand();
            while (_error is null && Current.Kind == kind)
            {
                _position++;
                left = new BinaryExpression(@operator, left, operand());
            }

            return left;
        }

        private Expression ParseComparison()
        {
            var left = ParseUnary();
            if (_error is not null || Current.Kind is not (TokenKind.Equal or TokenKind.NotEqual))
            {
                return left;
            }

            var @operator = Current.Kind == TokenKind.Equal ? BinaryOperator.Equal : BinaryOperator.NotEqual;
            _position++;
            var right = ParseUnary();
            if (_error is null && Current.Kind is TokenKind.Equal or TokenKind.NotEqual)
            {
                return Fail("Comparisons cannot be chained; combine them with && or ||.", Current.Offset);
            }

            return new BinaryExpression(@operator, left, right);
        }

        private Expression ParseUnary()
        {
            if (_error is not null)
            {
                return Placeholder;
            }

            if (_depth >= MaxDepth)
            {
                return Fail($"The expression is nested deeper than {MaxDepth} levels.", Current.Offset);
            }

            _depth++;
            Expression result;
            if (Current.Kind == TokenKind.Not)
            {
                _position++;
                result = new NotExpression(ParseUnary());
            }
            else
            {
                result = ParsePrimary();
            }

            _depth--;
            return result;
        }

        private Expression ParsePrimary()
        {
            var token = Current;
            if (token.Kind != TokenKind.End)
            {
                _position++;
            }

            return token.Kind switch
            {
                TokenKind.String => new LiteralExpression(Tokenizer.Unquote(token.Text)),
                TokenKind.Number => new LiteralExpression(token.Text),
                TokenKind.True => new LiteralExpression("true"),
                TokenKind.False => new LiteralExpression("false"),
                TokenKind.Null => new LiteralExpression(null),
                TokenKind.Path => ParsePath(token),
                TokenKind.OpenParenthesis => ParseGroup(token),
                TokenKind.End => Fail("The expression ends too early; a value is missing.", token.Offset),
                _ => Fail($"Expected a value but found '{token.Text}'.", token.Offset),
            };
        }

        private Expression ParseGroup(Token open)
        {
            var inner = ParseOr();
            if (_error is not null)
            {
                return inner;
            }

            if (Current.Kind != TokenKind.CloseParenthesis)
            {
                return Fail("The '(' is not closed.", open.Offset);
            }

            _position++;
            return inner;
        }

        private Expression ParsePath(Token token)
        {
            var segments = token.Text.Split('.');
            if (Array.Exists(segments, s => s.Length == 0 || !(char.IsAsciiLetter(s[0]) || s[0] == '_')))
            {
                return Fail($"'{token.Text}' is not a valid path; write names separated by single dots, such as vars.NAME.", token.Offset);
            }

            return new PathExpression(segments, token.Offset);
        }
    }
}
