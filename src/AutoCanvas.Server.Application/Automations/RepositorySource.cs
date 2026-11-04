namespace AutoCanvas.Server.Application.Automations;

/// <summary>Where a repository automation comes from (ADR-0021).</summary>
/// <param name="RepositoryId">The repository.</param>
/// <param name="RepositoryName">Its name in the folder.</param>
/// <param name="Path">The file, from the repository's root.</param>
/// <param name="Commit">The commit the current version was read at.</param>
public sealed record RepositorySource(Guid RepositoryId, string RepositoryName, string Path, string? Commit);
