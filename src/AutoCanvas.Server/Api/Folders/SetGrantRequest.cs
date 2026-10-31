using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Server.Api.Folders;

/// <summary>A role for one user or one group on a folder; replaces the role they had there.</summary>
/// <param name="Role">Viewer, Operator, Editor or Owner.</param>
/// <param name="UserId">The user; give either this or groupId.</param>
/// <param name="GroupId">The group; give either this or userId.</param>
internal sealed record SetGrantRequest(FolderRole Role, Guid? UserId = null, Guid? GroupId = null);
