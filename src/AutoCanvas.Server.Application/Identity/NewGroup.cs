namespace AutoCanvas.Server.Application.Identity;

/// <summary>A group to create.</summary>
/// <param name="Name">The group's name; unique regardless of case.</param>
/// <param name="Description">What the group is for.</param>
public sealed record NewGroup(string Name, string? Description = null);
