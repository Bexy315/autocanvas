namespace AutoCanvas.Server.Api.Identity;

/// <summary>A group to create.</summary>
/// <param name="Name">The group name; unique regardless of case.</param>
/// <param name="Description">What the group is for.</param>
internal sealed record CreateGroupRequest(string Name, string? Description = null);
