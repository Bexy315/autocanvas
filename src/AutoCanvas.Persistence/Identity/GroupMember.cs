namespace AutoCanvas.Persistence.Identity;

/// <summary>Membership of one user in one group.</summary>
public sealed class GroupMember
{
    /// <summary>The group.</summary>
    public required Guid GroupId { get; init; }

    /// <summary>The member.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Navigation to the group.</summary>
    public Group? Group { get; init; }

    /// <summary>Navigation to the user.</summary>
    public User? User { get; init; }
}
