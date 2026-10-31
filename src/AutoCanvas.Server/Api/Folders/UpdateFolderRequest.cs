namespace AutoCanvas.Server.Api.Folders;

/// <summary>Changes to a folder; omitted properties stay as they are.</summary>
/// <param name="Name">A new name.</param>
/// <param name="Description">A new description; an empty string removes it.</param>
internal sealed record UpdateFolderRequest(string? Name = null, string? Description = null);
