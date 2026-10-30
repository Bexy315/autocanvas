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

    /// <summary>
    /// Edit distance between two short strings, counting an insertion, deletion, substitution or
    /// swap of two adjacent characters as one edit (optimal string alignment), so "biuld" is one
    /// typo away from "build".
    /// </summary>
    public static int Distance(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        // Three rows suffice: the swap rule looks back two rows.
        var beforePrevious = new int[b.Length + 1];
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
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    current[j] = Math.Min(current[j], beforePrevious[j - 2] + 1);
                }
            }

            (beforePrevious, previous, current) = (previous, current, beforePrevious);
        }

        return previous[b.Length];
    }
}
