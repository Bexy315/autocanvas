namespace AutoCanvas.Persistence.Identity;

/// <summary>A set of users that permissions can be granted to as a whole.</summary>
public sealed class Group
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Column size of <see cref="Description"/>.</summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The group's name.</summary>
    public required string Name { get; set; }

    /// <summary>Upper-case form of <see cref="Name"/>; unique.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>What the group is for.</summary>
    public string? Description { get; set; }

    /// <summary>When the group was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The group's members.</summary>
    public ICollection<GroupMember> Members { get; } = [];
}
