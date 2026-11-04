using AutoCanvas.Forge.GitHub;
using AutoCanvas.Persistence.Repositories;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// Pages of a repository on its forge, for links in the UI: known for GitHub, whose addresses follow
/// one pattern; <see langword="null"/> for Generic Git.
/// </summary>
internal static class RepositoryLinks
{
    /// <summary>The page of the repository.</summary>
    public static Uri? Web(Repository repository) =>
        repository.Kind == RepositoryKind.GitHub ? GitHubNames.WebOf(repository.Url) : null;

    /// <summary>A file at a commit.</summary>
    public static Uri? File(Repository repository, string? commit, string path) =>
        Web(repository) is { } web && commit is not null ? new Uri($"{web}/blob/{commit}/{path}") : null;

    /// <summary>A commit.</summary>
    public static Uri? Commit(Repository repository, string? commit) =>
        Web(repository) is { } web && commit is not null ? new Uri($"{web}/commit/{commit}") : null;

    /// <summary>A pull request.</summary>
    public static Uri? PullRequest(Repository repository, int? number) =>
        Web(repository) is { } web && number is { } n ? new Uri($"{web}/pull/{n}") : null;
}
