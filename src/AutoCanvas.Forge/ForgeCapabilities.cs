namespace AutoCanvas.Forge;

/// <summary>
/// What a forge can do beyond reading files, so the server uses what is there and says when
/// something is missing (ADR-0021).
/// </summary>
/// <param name="CommitStatus">It shows a run's result at the commit.</param>
/// <param name="Comments">It takes comments on pull requests.</param>
/// <param name="PullRequests">It takes a branch with a commit and opens a pull request.</param>
public sealed record ForgeCapabilities(bool CommitStatus, bool Comments, bool PullRequests)
{
    /// <summary>Files and commits only.</summary>
    public static ForgeCapabilities None { get; } = new(CommitStatus: false, Comments: false, PullRequests: false);
}
