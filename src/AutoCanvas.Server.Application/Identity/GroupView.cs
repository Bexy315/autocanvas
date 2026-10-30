namespace AutoCanvas.Server.Application.Identity;

/// <summary>A group as listed.</summary>
/// <param name="Id">The group's id.</param>
/// <param name="Name">The group's name.</param>
/// <param name="Description">What the group is for.</param>
/// <param name="CreatedAt">When the group was created.</param>
/// <param name="MemberCount">How many users are in it.</param>
public sealed record GroupView(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt, int MemberCount);
