using System.Collections.Concurrent;
using AutoCanvas.Forge.GitHub;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// The installation tokens of the GitHub App, kept in memory until five minutes before they expire.
/// The token before the current one is remembered too, so that logs of jobs that checked out with it
/// are still masked.
/// </summary>
public sealed class GitHubTokens
{
    /// <summary>How long before it expires a token is replaced.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<long, (GitHubToken Current, string? Previous)> tokens = new();

    /// <summary>Forgets every token, as when the app changes.</summary>
    public void Clear() => tokens.Clear();

    /// <summary>The installation's current token, if it is valid long enough.</summary>
    internal string? Valid(long installationId, DateTimeOffset now) =>
        tokens.TryGetValue(installationId, out var entry) && entry.Current.ExpiresAt - Margin > now ? entry.Current.Token : null;

    /// <summary>Keeps a new token for the installation.</summary>
    internal void Store(long installationId, GitHubToken token) =>
        tokens.AddOrUpdate(installationId, (token, null), (_, old) => (token, string.Equals(old.Current.Token, token.Token, StringComparison.Ordinal) ? old.Previous : old.Current.Token));

    /// <summary>The installation's current and previous token.</summary>
    internal IReadOnlyList<string> Issued(long installationId)
    {
        if (!tokens.TryGetValue(installationId, out var entry))
        {
            return [];
        }

        return entry.Previous is { } previous ? [entry.Current.Token, previous] : [entry.Current.Token];
    }
}
