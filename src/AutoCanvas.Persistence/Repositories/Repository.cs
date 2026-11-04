using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Persistence.Repositories;

/// <summary>
/// A repository connected in a folder (ADR-0021): its <c>.autocanvas/automations/</c> files become
/// the folder's repository automations, read at its default branch.
/// </summary>
public sealed class Repository
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Column size of <see cref="Url"/> and of an automation's source path.</summary>
    public const int MaxUrlLength = 2048;

    /// <summary>Column size of <see cref="DefaultBranch"/>.</summary>
    public const int MaxBranchLength = 200;

    /// <summary>Column size of <see cref="UserName"/>.</summary>
    public const int MaxUserNameLength = 200;

    /// <summary>Column size of <see cref="LastError"/>.</summary>
    public const int MaxErrorLength = 2000;

    /// <summary>Column size of a commit SHA (SHA-256 repositories have 64 characters).</summary>
    public const int MaxCommitLength = 64;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The folder it is connected in; its roles decide who may use it.</summary>
    public required Guid FolderId { get; set; }

    /// <summary>Its name in the folder, unique regardless of case; <c>checkout</c> lists name it.</summary>
    public required string Name { get; set; }

    /// <summary>Upper-case form of <see cref="Name"/>.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>Which forge it lives on.</summary>
    public required RepositoryKind Kind { get; init; }

    /// <summary>Its clone address.</summary>
    public required Uri Url { get; set; }

    /// <summary>The branch its automations are read from; <see langword="null"/> for the repository's own default.</summary>
    public string? DefaultBranch { get; set; }

    /// <summary>The branch read at the last sync: the configured one or the repository's default.</summary>
    public string? ResolvedBranch { get; set; }

    /// <summary>The user name sent with the credential, if any.</summary>
    public string? UserName { get; set; }

    /// <summary>The token or password, encrypted like a secret (ADR-0013); <see langword="null"/> for public repositories.</summary>
    public string? CredentialCiphertext { get; set; }

    /// <summary>Which master key encrypted <see cref="CredentialCiphertext"/>.</summary>
    public string? CredentialKeyId { get; set; }

    /// <summary>When it was last read, successfully or not.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>The commit the automations were last read at.</summary>
    public string? LastCommit { get; set; }

    /// <summary>Why the last sync failed, in words for people; <see langword="null"/> when it worked.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// The files that are not (or no longer) automations of the folder and why, as JSON (path to
    /// problems), from the last sync.
    /// </summary>
    public string? Problems { get; set; }

    /// <summary>When it was connected.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Who connected it, kept when the user is deleted.</summary>
    public required string CreatedByName { get; init; }

    /// <summary>When its settings last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Navigation to the folder.</summary>
    public Folder? Folder { get; init; }
}
