using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>A role granted directly on a folder.</summary>
/// <param name="Id">The grant's id.</param>
/// <param name="SubjectType"><c>user</c> or <c>group</c>.</param>
/// <param name="SubjectId">The user or group.</param>
/// <param name="SubjectName">The user name or group name.</param>
/// <param name="Role">The role.</param>
/// <param name="CreatedAt">When the grant was first made.</param>
public sealed record GrantView(Guid Id, string SubjectType, Guid SubjectId, string SubjectName, FolderRole Role, DateTimeOffset CreatedAt);
