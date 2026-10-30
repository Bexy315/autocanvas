using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>A folder the actor may access, with what was needed to decide that.</summary>
/// <param name="Folder">The folder (tracked when the use case changes it).</param>
/// <param name="Role">The actor's effective role on it.</param>
/// <param name="Chain">The folders from the top level down to it, itself included.</param>
/// <param name="Grants">The actor's grants on the chain, own and through groups.</param>
internal sealed record AccessibleFolder(Folder Folder, FolderRole Role, IReadOnlyList<FolderChainEntry> Chain, IReadOnlyCollection<FolderGrantFact> Grants);
