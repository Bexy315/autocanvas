namespace AutoCanvas.Server.Api.Folders;

/// <summary>Where to move a folder.</summary>
/// <param name="ParentId">The new parent (needs Editor there), or null for the top level (needs Owner of the folder).</param>
internal sealed record MoveFolderRequest(Guid? ParentId);
