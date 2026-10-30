namespace AutoCanvas.Server.Application.Identity;

/// <summary>A group as shown in a user's memberships.</summary>
/// <param name="Id">The group's id.</param>
/// <param name="Name">The group's name.</param>
public sealed record GroupSummary(Guid Id, string Name);
