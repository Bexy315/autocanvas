namespace AutoCanvas.Server.Application.Forges;

/// <summary>A repository of an installation of the GitHub App to connect in a folder.</summary>
/// <param name="InstallationId">The installation it is reached through.</param>
/// <param name="Repository">Its <c>owner/name</c>.</param>
public sealed record NewGitHubRepository(long InstallationId, string Repository)
{
    /// <summary>Its name in the folder; the repository's name when unset.</summary>
    public string? Name { get; init; }

    /// <summary>The branch to read automations from; the repository's default when unset.</summary>
    public string? DefaultBranch { get; init; }

    /// <summary>How often to poll it, in minutes; 0 not to (the app's events then bring the pushes). Default: 5.</summary>
    public int? PollMinutes { get; init; }
}
