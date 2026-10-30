namespace AutoCanvas.Core.Expressions;

/// <summary>Splits expression text into tokens.</summary>
internal static class Tokenizer
{
    public static (List<Token> Tokens, ExpressionError? Error) Tokenize(string text)
    {
        var tokens = new List<Token>();
        var position = 0;
        while (true)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            if (position >= text.Length)
            {
                tokens.Add(new Token(TokenKind.End, string.Empty, position));
                return (tokens, null);
            }

            var (token, error) = Next(text, position);
            if (error is not null)
            {
                return (tokens, error);
            }

            tokens.Add(token);
            position = token.Offset + token.Text.Length;
        }
    }

    private static (Token Token, ExpressionError? Error) Next(string text, int position)
    {
        var c = text[position];
        var two = position + 1 < text.Length ? text.Substring(position, 2) : string.Empty;
        return two switch
        {
            "==" => (new Token(TokenKind.Equal, two, position), null),
            "!=" => (new Token(TokenKind.NotEqual, two, position), null),
            "&&" => (new Token(TokenKind.And, two, position), null),
            "||" => (new Token(TokenKind.Or, two, position), null),
            _ => c switch
            {
                '!' => (new Token(TokenKind.Not, "!", position), null),
                '(' => (new Token(TokenKind.OpenParenthesis, "(", position), null),
                ')' => (new Token(TokenKind.CloseParenthesis, ")", position), null),
                '\'' => ReadString(text, position),
                _ when char.IsAsciiDigit(c) || (c == '-' && position + 1 < text.Length && char.IsAsciiDigit(text[position + 1])) => (ReadNumber(text, position), null),
                _ when char.IsAsciiLetter(c) || c == '_' => (ReadWord(text, position), null),
                _ => (default, new ExpressionError($"Unexpected character '{c}'.", position)),
            },
        };
    }

    /// <summary>A quoted string; the token text keeps the quotes, <see cref="Unquote"/> gives the value.</summary>
    private static (Token Token, ExpressionError? Error) ReadString(string text, int start)
    {
        var position = start + 1;
        while (position < text.Length)
        {
            if (text[position] != '\'')
            {
                position++;
            }
            else if (position + 1 < text.Length && text[position + 1] == '\'')
            {
                position += 2;
            }
            else
            {
                return (new Token(TokenKind.String, text[start..(position + 1)], start), null);
            }
        }

        return (default, new ExpressionError("Text is missing its closing quote (').", start));
    }

    private static Token ReadNumber(string text, int start)
    {
        var position = start + 1;
        while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.'))
        {
            position++;
        }

        return new Token(TokenKind.Number, text[start..position], start);
    }

    private static Token ReadWord(string text, int start)
    {
        var position = start;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] is '_' or '-' or '.'))
        {
            position++;
        }

        var word = text[start..position];
        var kind = word switch
        {
            "true" => TokenKind.True,
            "false" => TokenKind.False,
            "null" => TokenKind.Null,
            _ => TokenKind.Path,
        };
        return new Token(kind, word, start);
    }

    /// <summary>The value of a string token: without quotes, with <c>''</c> unescaped.</summary>
    public static string Unquote(string tokenText) => tokenText[1..^1].Replace("''", "'", StringComparison.Ordinal);
}
