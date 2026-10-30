namespace AutoCanvas.Core.Parsing;

/// <summary>"Did you mean" hints for misspelled keys.</summary>
internal static class Suggestions
{
    /// <summary>Renamed keys of the format the origin engine used, with what replaced them.</summary>
    private static readonly Dictionary<string, string> Renamed = new(StringComparer.Ordinal)
    {
        ["trigger"] = "triggers",
        ["depends_on"] = "needs",
        ["runner_labels"] = "runs_on",
        ["build_environment"] = "image",
        ["config"] = "with",
        ["on_push"] = "push",
        ["cron"] = "schedule",
    };

    /// <summary>A sentence suggesting a known key close to <paramref name="key"/>, or an empty string.</summary>
    public static string ForKey(string key, IEnumerable<string> known)
    {
        if (Renamed.TryGetValue(key, out var replacement))
        {
            return $" Use '{replacement}' instead.";
        }

        var best = known
            .Select(candidate => (Candidate: candidate, Distance: Distance(key, candidate)))
            .Where(c => c.Distance <= Math.Max(1, c.Candidate.Length / 3))
            .OrderBy(c => c.Distance)
            .Select(c => c.Candidate)
            .FirstOrDefault();
        return best is null ? string.Empty : $" Did you mean '{best}'?";
    }

    /// <summary>Levenshtein distance between two short strings.</summary>
    public static int Distance(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
