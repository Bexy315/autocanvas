namespace AutoCanvas.Persistence.Repositories;

/// <summary>
/// The commit a branch or tag of a connected repository was last seen at. A push is handled once:
/// whoever moves the row to the new commit first (a webhook or a poll) starts its runs.
/// </summary>
public sealed class RepositoryRef
{
    /// <summary>Column size of <see cref="Ref"/>.</summary>
    public const int MaxRefLength = 255;

    /// <summary>The repository.</summary>
    public required Guid RepositoryId { get; init; }

    /// <summary>The full ref, such as <c>refs/heads/main</c> or <c>refs/tags/v1.0</c>.</summary>
    public required string Ref { get; init; }

    /// <summary>The commit it pointed to.</summary>
    public required string Commit { get; set; }
}
