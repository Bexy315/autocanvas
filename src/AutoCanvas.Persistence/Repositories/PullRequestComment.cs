namespace AutoCanvas.Persistence.Repositories;

/// <summary>
/// The comment AutoCanvas keeps on a pull request with the results of its runs, updated by later
/// runs: one per pull request and repository.
/// </summary>
public sealed class PullRequestComment
{
    /// <summary>The repository the pull request goes into.</summary>
    public required Guid RepositoryId { get; init; }

    /// <summary>The pull request's number.</summary>
    public required int Number { get; init; }

    /// <summary>The forge's id of the comment.</summary>
    public required long CommentId { get; set; }

    /// <summary>When it was last written.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
