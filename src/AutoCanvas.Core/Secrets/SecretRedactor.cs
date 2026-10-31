namespace AutoCanvas.Core.Secrets;

/// <summary>
/// Replaces secret values with <see cref="Marker"/> before text is logged or stored.
/// </summary>
/// <remarks>
/// It belongs where text is written down, not where it is displayed: a secret resolved into a
/// header or a script is in everything that echoes them, and redacting only when a log is shown
/// would leave the value in the database. Deliberately plain substring replacement of every known
/// value: it cannot recognise a secret that was transformed on the way (base64, split across
/// lines), and does not pretend to, but it catches the value arriving verbatim, which is what
/// happens.
/// </remarks>
public sealed class SecretRedactor
{
    /// <summary>What replaces a secret.</summary>
    public const string Marker = "***";

    /// <summary>
    /// Shortest value worth redacting: one or two characters would match all over a log and hide
    /// the failure someone is reading it for.
    /// </summary>
    public const int MinimumLength = 4;

    private readonly string[] values;

    /// <summary>Creates a redactor for these secret values.</summary>
    public SecretRedactor(IEnumerable<string?> secretValues)
    {
        ArgumentNullException.ThrowIfNull(secretValues);

        // Longest first: a secret that contains another must be replaced whole, otherwise the
        // shorter one is substituted inside it and leaves the rest visible.
        values = [.. secretValues
            .OfType<string>()
            .Where(v => v.Length >= MinimumLength)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(v => v.Length)];
    }

    /// <summary>Whether there is anything to redact.</summary>
    public bool IsEmpty => values.Length == 0;

    /// <summary>The text with every known secret value replaced by <see cref="Marker"/>.</summary>
    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        foreach (var value in values)
        {
            text = text.Replace(value, Marker, StringComparison.Ordinal);
        }

        return text;
    }
}
