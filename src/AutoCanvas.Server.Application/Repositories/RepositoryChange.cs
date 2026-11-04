namespace AutoCanvas.Server.Application.Repositories;

/// <summary>Changes to a repository; what is left out (or <see langword="null"/>) stays as it is.</summary>
public sealed record RepositoryChange
{
    /// <summary>A new name in the folder.</summary>
    public string? Name { get; init; }

    /// <summary>Another branch to read automations from; empty for the repository's own default.</summary>
    public string? DefaultBranch { get; init; }

    /// <summary>Another user name; empty for none.</summary>
    public string? UserName { get; init; }

    /// <summary>Another token or password; empty to remove it.</summary>
    public string? Secret { get; init; }

    /// <summary>How often to poll it, in minutes; 0 not to poll (pushes then come only by webhook).</summary>
    public int? PollMinutes { get; init; }
}
