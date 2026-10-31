using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>A scope the actor may use, with what the rows and the audit need.</summary>
/// <param name="Key">The scope column's value.</param>
/// <param name="FolderId">The folder, for folder scope.</param>
/// <param name="AutomationId">The automation, for automation scope.</param>
/// <param name="AuditFolderId">The folder audit events belong to.</param>
/// <param name="Role">The actor's role there.</param>
internal sealed record ResolvedScope(string Key, Guid? FolderId, Guid? AutomationId, Guid? AuditFolderId, FolderRole Role);
