namespace AutoCanvas.Server.Application.Folders;

/// <summary>One folder on the way from the top level down to a folder.</summary>
/// <param name="Id">The folder's id.</param>
/// <param name="Name">The folder's name, for breadcrumbs.</param>
/// <param name="IsRestricted">Whether grants from above stop here.</param>
public sealed record FolderChainEntry(Guid Id, string Name, bool IsRestricted);
