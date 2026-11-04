using AutoCanvas.Forge.GitHub;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>The instance's GitHub App, without its secrets.</summary>
/// <param name="AppId">GitHub's id of the app.</param>
/// <param name="Slug">Its name in addresses.</param>
/// <param name="Name">Its name.</param>
/// <param name="OwnerLogin">The account or organization that owns it.</param>
/// <param name="HtmlUrl">Its page on GitHub.</param>
/// <param name="InstallUrl">Where it is installed on another account.</param>
/// <param name="WebAddress">GitHub's web address.</param>
/// <param name="ApiAddress">GitHub's API address.</param>
/// <param name="WebhookUrl">The address GitHub sends its events to; <see langword="null"/> while the instance has no public address.</param>
/// <param name="Installations">Where it is installed.</param>
/// <param name="InstallationsError">Why the installations could not be read; <see langword="null"/> when they could.</param>
/// <param name="CreatedAt">When it was set up.</param>
/// <param name="CreatedByName">Who set it up.</param>
public sealed record GitHubAppView(
    long AppId,
    string Slug,
    string Name,
    string OwnerLogin,
    Uri HtmlUrl,
    Uri InstallUrl,
    Uri WebAddress,
    Uri ApiAddress,
    Uri? WebhookUrl,
    IReadOnlyList<GitHubInstallation> Installations,
    string? InstallationsError,
    DateTimeOffset CreatedAt,
    string CreatedByName);
