namespace AutoCanvas.Server.Application.Folders;

/// <summary>A folder above the current one, for orientation.</summary>
/// <param name="Id">The folder's id, or <see langword="null"/> when the caller may not open it.</param>
/// <param name="Name">The folder's name.</param>
public sealed record FolderCrumb(Guid? Id, string Name);
