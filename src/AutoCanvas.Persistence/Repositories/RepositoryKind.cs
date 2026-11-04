namespace AutoCanvas.Persistence.Repositories;

/// <summary>Which forge a repository lives on (ADR-0021).</summary>
public enum RepositoryKind
{
    /// <summary>Any Git repository over HTTP(S).</summary>
    GenericGit,

    /// <summary>A repository of the instance's GitHub App.</summary>
    GitHub,
}
