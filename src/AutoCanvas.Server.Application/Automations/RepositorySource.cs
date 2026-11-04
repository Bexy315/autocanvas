namespace AutoCanvas.Server.Application.Automations;

/// <summary>Where a repository automation comes from (ADR-0021).</summary>
/// <param name="RepositoryId">The repository.</param>
/// <param name="RepositoryName">Its name in the folder.</param>
/// <param name="Path">The file, from the repository's root.</param>
/// <param name="Commit">The commit the current version was read at.</param>
public sealed record RepositorySource(Guid RepositoryId, string RepositoryName, string Path, string? Commit)
{
    /// <summary>Which forge it lives on: <c>GenericGit</c> or <c>GitHub</c>.</summary>
    public string Kind { get; init; } = "GenericGit";

    /// <summary>Whether a change made in AutoCanvas can be proposed as a pull request (GitHub).</summary>
    public bool ProposesChanges { get; init; }

    /// <summary>The page of the repository on its forge, when known.</summary>
    public Uri? WebUrl { get; init; }

    /// <summary>The file at <see cref="Commit"/> on the forge, when known.</summary>
    public Uri? FileUrl { get; init; }

    /// <summary>The commit on the forge, when known.</summary>
    public Uri? CommitUrl { get; init; }

    /// <summary>The source of an automation from <paramref name="repository"/>.</summary>
    internal static RepositorySource Of(Persistence.Repositories.Repository repository, string path, string? commit) =>
        new(repository.Id, repository.Name, path, commit)
        {
            Kind = repository.Kind.ToString(),
            ProposesChanges = repository.Kind == Persistence.Repositories.RepositoryKind.GitHub,
            WebUrl = Repositories.RepositoryLinks.Web(repository),
            FileUrl = Repositories.RepositoryLinks.File(repository, commit, path),
            CommitUrl = Repositories.RepositoryLinks.Commit(repository, commit),
        };
}
