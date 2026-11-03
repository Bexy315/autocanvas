namespace AutoCanvas.Server.Application.Triggers;

/// <summary>A new webhook address; the token and the signing secret are shown only now.</summary>
/// <param name="Path">The path to call with POST, such as <c>/api/v1/hooks/ach_…</c>.</param>
/// <param name="Token">The token in the path.</param>
/// <param name="SigningSecret">The secret calls are signed with; <see langword="null"/> for unsigned calls.</param>
/// <param name="Hint">The token's start and end, as shown later.</param>
/// <param name="Enabled">Whether the current version allows webhook calls yet.</param>
public sealed record CreatedWebhookView(string Path, string Token, string? SigningSecret, string Hint, bool Enabled)
{
    /// <summary>
    /// The whole address, from the installation's public address; <see langword="null"/> while none
    /// is set, so clients put the path after the address they reach the server at.
    /// </summary>
    public Uri? Url { get; init; }
}
