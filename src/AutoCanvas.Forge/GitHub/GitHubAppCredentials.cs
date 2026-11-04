namespace AutoCanvas.Forge.GitHub;

/// <summary>A GitHub App GitHub created from a manifest, with its secrets, as GitHub returns them once.</summary>
/// <param name="Id">The app's id.</param>
/// <param name="Slug">Its name in addresses.</param>
/// <param name="Name">Its name.</param>
/// <param name="ClientId">Its OAuth client id.</param>
/// <param name="ClientSecret">Its OAuth client secret.</param>
/// <param name="WebhookSecret">The secret its webhook calls are signed with.</param>
/// <param name="PrivateKey">Its private key in PEM.</param>
/// <param name="OwnerLogin">The account or organization that owns it.</param>
/// <param name="HtmlUrl">Its page on GitHub.</param>
public sealed record GitHubAppCredentials(
    long Id,
    string Slug,
    string Name,
    string ClientId,
    string ClientSecret,
    string WebhookSecret,
    string PrivateKey,
    string OwnerLogin,
    Uri HtmlUrl)
{
    /// <inheritdoc />
    public override string ToString() => $"GitHubAppCredentials {{ Id = {Id}, Slug = {Slug}, OwnerLogin = {OwnerLogin} }}";
}
