using System.Runtime.InteropServices;
using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>A role the subject holds on one folder, directly or through a group.</summary>
/// <param name="FolderId">The folder of the grant.</param>
/// <param name="Role">The role granted there.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct FolderGrantFact(Guid FolderId, FolderRole Role);
