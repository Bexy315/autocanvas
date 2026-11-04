namespace AutoCanvas.Forge;

/// <summary>
/// A place repositories live (ADR-0021). Every forge reads files; what else it can do, it says in
/// <see cref="Capabilities"/>. Methods throw <see cref="ForgeException"/> when the forge refuses or
/// cannot be reached.
/// </summary>
public interface IForge
{
    /// <summary>Which forge this is.</summary>
    ForgeKind Kind { get; }

    /// <summary>What it can do beyond reading files.</summary>
    ForgeCapabilities Capabilities { get; }

    /// <summary>The commit a branch points to; the repository's default branch when <paramref name="branch"/> is <see langword="null"/>.</summary>
    Task<ResolvedBranch> ResolveAsync(RepositoryAccess access, string? branch, CancellationToken cancellationToken);

    /// <summary>
    /// The automation files at a branch or tag (and the commit it points to now):
    /// <paramref name="gitRef"/> is a branch's name or a full ref, <c>refs/heads/...</c> or
    /// <c>refs/tags/...</c>.
    /// </summary>
    Task<RepositorySnapshot> ReadAutomationsAsync(RepositoryAccess access, string gitRef, CancellationToken cancellationToken);

    /// <summary>The repository's branches and tags (<c>refs/heads/...</c>, <c>refs/tags/...</c>) and the commit each points to.</summary>
    Task<IReadOnlyDictionary<string, string>> ListRefsAsync(RepositoryAccess access, CancellationToken cancellationToken);

    /// <summary>
    /// The files that differ between two commits of a branch or tag, or <see langword="null"/> when
    /// that is not known, such as for a new branch or when <paramref name="before"/> is far back.
    /// </summary>
    Task<IReadOnlyList<string>?> ChangedFilesAsync(RepositoryAccess access, string gitRef, string before, string after, CancellationToken cancellationToken);
}
