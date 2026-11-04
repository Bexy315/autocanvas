namespace AutoCanvas.Core.Matching;

/// <summary>
/// The patterns of branch, tag and path filters: <c>*</c> matches any characters but <c>/</c>,
/// <c>**</c> any characters including <c>/</c> (and <c>**/</c> also no folder at all), <c>?</c>
/// one character but <c>/</c>; everything else matches itself, case-sensitively. A pattern
/// matches the whole text, not a part of it.
/// </summary>
public static class Glob
{
    /// <summary>Whether <paramref name="text"/> matches <paramref name="pattern"/>.</summary>
    public static bool IsMatch(string pattern, string text)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(text);

        // Each (pattern position, text position) pair is decided once, so the work stays at
        // length(pattern) × length(text) whatever the stars: 0 = not yet known, 1 = no, 2 = yes.
        var known = new byte[(pattern.Length + 1) * (text.Length + 1)];
        return Match(pattern, text, 0, 0, known);
    }

    /// <summary>Whether <paramref name="text"/> matches any of <paramref name="patterns"/>.</summary>
    public static bool MatchesAny(IEnumerable<string> patterns, string text)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        return patterns.Any(p => IsMatch(p, text));
    }

    private static bool Match(string pattern, string text, int i, int j, byte[] known)
    {
        var slot = (i * (text.Length + 1)) + j;
        if (known[slot] != 0)
        {
            return known[slot] == 2;
        }

        var result = Decide(pattern, text, i, j, known);
        known[slot] = result ? (byte)2 : (byte)1;
        return result;
    }

    private static bool Decide(string pattern, string text, int i, int j, byte[] known)
    {
        if (i == pattern.Length)
        {
            return j == text.Length;
        }

        var more = j < text.Length;
        switch (pattern[i])
        {
            case '*' when i + 1 < pattern.Length && pattern[i + 1] == '*':
                var rest = i + 2;
                return Match(pattern, text, rest, j, known)
                    || (rest < pattern.Length && pattern[rest] == '/' && Match(pattern, text, rest + 1, j, known))
                    || (more && Match(pattern, text, i, j + 1, known));
            case '*':
                return Match(pattern, text, i + 1, j, known) || (more && text[j] != '/' && Match(pattern, text, i, j + 1, known));
            case '?':
                return more && text[j] != '/' && Match(pattern, text, i + 1, j + 1, known);
            default:
                return more && text[j] == pattern[i] && Match(pattern, text, i + 1, j + 1, known);
        }
    }
}
