namespace AutoCanvas.Server.Application.Identity;

/// <summary>A group with its members, ordered by user name.</summary>
/// <param name="Id">The group's id.</param>
/// <param name="Name">The group's name.</param>
/// <param name="Description">What the group is for.</param>
/// <param name="CreatedAt">When the group was created.</param>
/// <param name="Members">The users in the group.</param>
public sealed record GroupDetails(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt, IReadOnlyList<UserSummary> Members);
