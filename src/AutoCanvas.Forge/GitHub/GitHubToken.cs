namespace AutoCanvas.Forge.GitHub;

/// <summary>An installation token: acts as the app on the installation's repositories until it expires.</summary>
/// <param name="Token">The token.</param>
/// <param name="ExpiresAt">When it expires (an hour after it was made).</param>
public sealed record GitHubToken(string Token, DateTimeOffset ExpiresAt)
{
    /// <inheritdoc />
    public override string ToString() => $"GitHubToken {{ ExpiresAt = {ExpiresAt:O} }}";
}
