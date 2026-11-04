namespace AutoCanvas.Forge.GitHub;

/// <summary>A GitHub App as it describes itself.</summary>
/// <param name="Id">The app's id.</param>
/// <param name="Slug">Its name in addresses.</param>
/// <param name="Name">Its name.</param>
/// <param name="ClientId">Its OAuth client id.</param>
/// <param name="OwnerLogin">The account or organization that owns it.</param>
/// <param name="HtmlUrl">Its page on GitHub.</param>
public sealed record GitHubAppInfo(long Id, string Slug, string Name, string? ClientId, string OwnerLogin, Uri HtmlUrl);
