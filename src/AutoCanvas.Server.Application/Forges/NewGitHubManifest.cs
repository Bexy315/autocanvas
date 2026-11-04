namespace AutoCanvas.Server.Application.Forges;

/// <summary>Where to create the GitHub App.</summary>
public sealed record NewGitHubManifest
{
    /// <summary>The organization to create it in; the signed-in GitHub account when unset.</summary>
    public string? Organization { get; init; }

    /// <summary>The web address of a GitHub Enterprise Server; <c>https://github.com</c> when unset.</summary>
    public string? WebAddress { get; init; }
}
