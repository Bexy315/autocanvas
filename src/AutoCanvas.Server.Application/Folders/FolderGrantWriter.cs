using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>
/// Creates or changes a grant within the caller's unit of work and records it. Shared by the grant
/// API and by the folder use cases that keep their actor's access (create, restrict, move).
/// </summary>
internal static class FolderGrantWriter
{
    /// <summary>Sets the subject's role on the folder and returns the role it had before.</summary>
    public static async Task<FolderRole> SetAsync(
        AutoCanvasDbContext database,
        IAuditLog audit,
        Guid folderId,
        GrantSubject subject,
        FolderRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = database.FolderGrants.Local.FirstOrDefault(g => Matches(g, folderId, subject))
            ?? await database.FolderGrants
                .FirstOrDefaultAsync(g => g.FolderId == folderId && g.UserId == subject.UserId && g.GroupId == subject.GroupId, cancellationToken)
                .ConfigureAwait(false);
        var previous = existing?.Role ?? FolderRole.None;
        if (previous == role)
        {
            return previous;
        }

        if (existing is null)
        {
            database.FolderGrants.Add(new FolderGrant
            {
                Id = Guid.CreateVersion7(now),
                FolderId = folderId,
                UserId = subject.UserId,
                GroupId = subject.GroupId,
                Role = role,
                CreatedAt = now,
            });
        }
        else
        {
            existing.Role = role;
        }

        audit.Record(Entry(AuditActions.FolderGrantSet, folderId, subject, role, ("previousRole", previous == FolderRole.None ? null : previous.ToString())));
        return previous;
    }

    /// <summary>The audit entry for a grant change on a folder.</summary>
    public static AuditEntry Entry(string action, Guid folderId, GrantSubject subject, FolderRole role, params (string Key, object? Value)[] more) => new(action)
    {
        TargetType = "folder",
        TargetId = folderId.ToString(),
        FolderId = folderId,
        Details = AuditEntry.DetailsOf(
        [
            ("subjectType", subject.Type),
            ("subjectId", subject.Id),
            ("subjectName", subject.Name),
            ("role", role.ToString()),
            .. more,
        ]),
    };

    private static bool Matches(FolderGrant grant, Guid folderId, GrantSubject subject) =>
        grant.FolderId == folderId && grant.UserId == subject.UserId && grant.GroupId == subject.GroupId;
}
