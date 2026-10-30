namespace AutoCanvas.Server.Api.Identity;

/// <summary>Changes to a group; omitted properties stay as they are.</summary>
/// <param name="Name">A new name.</param>
/// <param name="Description">A new description; an empty string removes it.</param>
internal sealed record UpdateGroupRequest(string? Name = null, string? Description = null);
