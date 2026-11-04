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

    /// <summary>The automation files at a branch (and the commit it points to now).</summary>
    Task<RepositorySnapshot> ReadAutomationsAsync(RepositoryAccess access, string branch, CancellationToken cancellationToken);
}
