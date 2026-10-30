namespace AutoCanvas.Reports.Xml;

/// <summary>Cleaning up texts from reports.</summary>
internal static class Text
{
    /// <summary>Trimmed text, or <see langword="null"/> when there is none.</summary>
    public static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Cuts <paramref name="value"/> to at most <paramref name="max"/> characters, marking the cut with an ellipsis.</summary>
    public static string? Cut(string? value, int max) =>
        value is null || value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");

    /// <summary>The first non-empty line, used as a message when a format only gives a full text.</summary>
    public static string? FirstLine(string? value) =>
        value?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);

    /// <summary>Joins the non-empty parts with a newline, or <see langword="null"/> if there are none.</summary>
    public static string? Join(params string?[] parts)
    {
        var present = parts.Select(NonEmpty).OfType<string>().ToList();
        return present.Count == 0 ? null : string.Join('\n', present);
    }
}
