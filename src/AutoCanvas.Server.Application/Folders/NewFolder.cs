namespace AutoCanvas.Server.Application.Folders;

/// <summary>A folder to create.</summary>
/// <param name="Name">The name; unique among its siblings regardless of case.</param>
/// <param name="ParentId">The folder to create it in; <see langword="null"/> for the top level.</param>
/// <param name="Description">What the folder is for.</param>
public sealed record NewFolder(string Name, Guid? ParentId = null, string? Description = null);
