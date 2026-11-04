namespace AutoCanvas.Persistence.Repositories;

/// <summary>
/// A pull request event a forge reported, waiting to be handled by the server in the background
/// like a <see cref="RepositoryPush"/>. Handling takes the row out.
/// </summary>
public sealed class RepositoryPullRequest
{
    /// <summary>Column size of <see cref="Action"/>.</summary>
    public const int MaxActionLength = 40;

    /// <summary>Column size of <see cref="Title"/>.</summary>
    public const int MaxTitleLength = 500;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The repository the pull request goes into.</summary>
    public required Guid RepositoryId { get; init; }

    /// <summary>The pull request's number.</summary>
    public required int Number { get; init; }

    /// <summary>What happened, such as <c>opened</c> or <c>synchronize</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The commit at the head of the pull request.</summary>
    public required string HeadSha { get; init; }

    /// <summary>The branch the pull request comes from.</summary>
    public required string HeadBranch { get; init; }

    /// <summary>The branch it goes into.</summary>
    public required string BaseBranch { get; init; }

    /// <summary>Its title.</summary>
    public required string Title { get; init; }

    /// <summary>Who opened it.</summary>
    public required string Author { get; init; }

    /// <summary>Its page on the forge.</summary>
    public Uri? Url { get; init; }

    /// <summary>Whether it comes from a fork, whose code gets no secrets.</summary>
    public required bool Fork { get; init; }

    /// <summary>When the event arrived.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}
