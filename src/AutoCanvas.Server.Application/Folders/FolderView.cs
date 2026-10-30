using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>A folder as the caller sees it.</summary>
/// <param name="Id">The folder's id.</param>
/// <param name="Name">The folder's name.</param>
/// <param name="Description">What the folder is for.</param>
/// <param name="ParentId">The folder above; <see langword="null"/> at the top level.</param>
/// <param name="IsRestricted">Whether grants from above stop applying here.</param>
/// <param name="Path">The folders above, from the top level down (names; ids only where the caller may open them).</param>
/// <param name="MyRole">The caller's role on this folder.</param>
/// <param name="CreatedAt">When the folder was created.</param>
/// <param name="Version">Changes with every change of the folder; the ETag of the API.</param>
public sealed record FolderView(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentId,
    bool IsRestricted,
    IReadOnlyList<FolderCrumb> Path,
    FolderRole MyRole,
    DateTimeOffset CreatedAt,
    int Version);
