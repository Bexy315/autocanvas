using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>A subfolder as listed under its parent.</summary>
/// <param name="Id">The folder's id.</param>
/// <param name="Name">The folder's name.</param>
/// <param name="Description">What the folder is for.</param>
/// <param name="IsRestricted">Whether grants from above stop applying here.</param>
/// <param name="MyRole">The caller's role on this folder.</param>
public sealed record FolderSummary(Guid Id, string Name, string? Description, bool IsRestricted, FolderRole MyRole);
