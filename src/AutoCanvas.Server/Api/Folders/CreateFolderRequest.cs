namespace AutoCanvas.Server.Api.Folders;

/// <summary>A folder to create.</summary>
/// <param name="Name">The name: 1 to 100 characters, no slash; unique among its siblings regardless of case.</param>
/// <param name="ParentId">The folder to create it in (needs Editor there); omitted or null for the top level, where the creator becomes Owner.</param>
/// <param name="Description">What the folder is for.</param>
internal sealed record CreateFolderRequest(string Name, Guid? ParentId = null, string? Description = null);
