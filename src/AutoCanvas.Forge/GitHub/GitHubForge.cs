namespace AutoCanvas.Forge.GitHub;

/// <summary>
/// GitHub (ADR-0022): repositories are read with git over https like any Git repository, with an
/// installation token of the GitHub App as the credential. Statuses, comments and pull requests come
/// with the pull request step of S5.
/// </summary>
public sealed class GitHubForge(TimeSpan timeout) : IForge
{
    private readonly GenericGitForge git = new(timeout);

    /// <inheritdoc />
    public ForgeKind Kind => ForgeKind.GitHub;

    /// <inheritdoc />
    public ForgeCapabilities Capabilities => ForgeCapabilities.None;

    /// <inheritdoc />
    public Task<ResolvedBranch> ResolveAsync(RepositoryAccess access, string? branch, CancellationToken cancellationToken) =>
        git.ResolveAsync(access, branch, cancellationToken);

    /// <inheritdoc />
    public Task<RepositorySnapshot> ReadAutomationsAsync(RepositoryAccess access, string gitRef, CancellationToken cancellationToken) =>
        git.ReadAutomationsAsync(access, gitRef, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ListRefsAsync(RepositoryAccess access, CancellationToken cancellationToken) =>
        git.ListRefsAsync(access, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>?> ChangedFilesAsync(RepositoryAccess access, string gitRef, string before, string after, CancellationToken cancellationToken) =>
        git.ChangedFilesAsync(access, gitRef, before, after, cancellationToken);
}
