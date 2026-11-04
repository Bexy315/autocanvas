namespace AutoCanvas.Server.Application.Repositories;

/// <summary>A new webhook address of a repository; the token and the secret are shown only now.</summary>
/// <param name="Path">The path the forge calls with POST, such as <c>/api/v1/hooks/acg_…</c>.</param>
/// <param name="Token">The token in the path.</param>
/// <param name="Secret">The secret the forge signs with (GitHub, Gitea) or sends (GitLab).</param>
/// <param name="Hint">The token's start and end, as shown later.</param>
public sealed record CreatedRepositoryWebhookView(string Path, string Token, string Secret, string Hint)
{
    /// <summary>
    /// The whole address, from the installation's public address; <see langword="null"/> while none
    /// is set, so clients put the path after the address they reach the server at.
    /// </summary>
    public Uri? Url { get; init; }
}
