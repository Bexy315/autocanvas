using System.Collections.Concurrent;
using AutoCanvas.Forge.GitHub;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// The installation tokens of the GitHub App, kept in memory until five minutes before they expire:
/// one per installation for the server's own use, and one per installation and repository for
/// checkouts, which may only read that repository. The token before the current one is remembered
/// too, so that logs of jobs that checked out with it are still masked.
/// </summary>
public sealed class GitHubTokens
{
    /// <summary>How long before it expires a token is replaced.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<(long Installation, long? Repository), (GitHubToken Current, string? Previous)> tokens = new();

    /// <summary>Forgets every token, as when the app changes.</summary>
    public void Clear() => tokens.Clear();

    /// <summary>The current token, if it is valid long enough; with a repository, its checkout token.</summary>
    internal string? Valid(long installationId, DateTimeOffset now, long? repositoryId = null) =>
        tokens.TryGetValue((installationId, repositoryId), out var entry) && entry.Current.ExpiresAt - Margin > now ? entry.Current.Token : null;

    /// <summary>Keeps a new token; with a repository, as its checkout token.</summary>
    internal void Store(long installationId, GitHubToken token, long? repositoryId = null) =>
        tokens.AddOrUpdate((installationId, repositoryId), (token, null), (_, old) => (token, string.Equals(old.Current.Token, token.Token, StringComparison.Ordinal) ? old.Previous : old.Current.Token));

    /// <summary>Every current and previous token of the installation, its checkout tokens included.</summary>
    internal IReadOnlyList<string> Issued(long installationId)
    {
        var issued = new List<string>();
        foreach (var (key, entry) in tokens)
        {
            if (key.Installation != installationId)
            {
                continue;
            }

            issued.Add(entry.Current.Token);
            if (entry.Previous is { } previous)
            {
                issued.Add(previous);
            }
        }

        return issued;
    }
}
