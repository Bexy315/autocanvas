using AutoCanvas.Persistence.Identity;

namespace AutoCanvas.Persistence.Folders;

/// <summary>A role on a folder for exactly one user or one group.</summary>
public sealed class FolderGrant
{
    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The folder the role applies to (and, unless restricted, below).</summary>
    public required Guid FolderId { get; init; }

    /// <summary>The user who holds the role; set when <see cref="GroupId"/> is not.</summary>
    public Guid? UserId { get; init; }

    /// <summary>The group whose members hold the role; set when <see cref="UserId"/> is not.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>The role.</summary>
    public required FolderRole Role { get; set; }

    /// <summary>When the grant was first made.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Navigation to the folder.</summary>
    public Folder? Folder { get; init; }

    /// <summary>Navigation to the user.</summary>
    public User? User { get; init; }

    /// <summary>Navigation to the group.</summary>
    public Group? Group { get; init; }
}
