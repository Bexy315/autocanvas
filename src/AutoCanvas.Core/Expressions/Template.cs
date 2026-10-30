using System.Text;

namespace AutoCanvas.Core.Expressions;

/// <summary>
/// Text with embedded <c>${{ … }}</c> expressions, such as a script or a node input. Rendering
/// replaces each expression with its value.
/// </summary>
/// <param name="Parts">Literal text and expressions, in order.</param>
public sealed record Template(IReadOnlyList<TemplatePart> Parts)
{
    /// <summary>Whether the text contains any expression.</summary>
    public bool HasExpressions => Parts.Any(p => p is ExpressionPart);

    /// <summary>Splits <paramref name="text"/> into literal text and parsed expressions.</summary>
    /// <param name="text">The text as written.</param>
    /// <returns>The template, and all syntax errors with offsets in <paramref name="text"/>.</returns>
    public static (Template Template, IReadOnlyList<ExpressionError> Errors) Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var parts = new List<TemplatePart>();
        var errors = new List<ExpressionError>();
        var position = 0;
        while (position < text.Length)
        {
            var open = text.IndexOf("${{", position, StringComparison.Ordinal);
            if (open < 0)
            {
                parts.Add(new TextPart(text[position..]));
                break;
            }

            if (open > position)
            {
                parts.Add(new TextPart(text[position..open]));
            }

            position = ParseExpression(text, open, parts, errors);
        }

        return (new Template(parts), errors);
    }

    /// <summary>Renders the template, reading values from <paramref name="resolver"/>; a <c>null</c> value renders as empty text.</summary>
    /// <param name="resolver">Supplies the values expressions read.</param>
    /// <returns>The text with every expression replaced.</returns>
    public string Render(IValueResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var result = new StringBuilder();
        foreach (var part in Parts)
        {
            result.Append(part switch
            {
                TextPart text => text.Text,
                ExpressionPart expression => ExpressionEvaluator.Evaluate(expression.Expression, resolver),
                _ => throw new InvalidOperationException($"Unknown template part {part.GetType().Name}."),
            });
        }

        return result.ToString();
    }

    /// <summary>Parses the expression starting at <paramref name="open"/> and returns where the text continues.</summary>
    private static int ParseExpression(string text, int open, List<TemplatePart> parts, List<ExpressionError> errors)
    {
        var close = text.IndexOf("}}", open + 3, StringComparison.Ordinal);
        if (close < 0)
        {
            errors.Add(new ExpressionError("'${{' is not closed with '}}'.", open));
            parts.Add(new TextPart(text[open..]));
            return text.Length;
        }

        var (expression, error) = ExpressionSyntax.Parse(text[(open + 3)..close]);
        if (error is not null)
        {
            errors.Add(error with { Offset = error.Offset + open + 3 });
            parts.Add(new TextPart(text[open..(close + 2)]));
        }
        else
        {
            parts.Add(new ExpressionPart(expression!, open));
        }

        return close + 2;
    }
}
