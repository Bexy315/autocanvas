using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>Who has which role directly on a folder. Managing grants needs the Owner role.</summary>
public sealed class FolderGrantService
{
    private readonly AutoCanvasDbContext database;
    private readonly IAuditLog audit;
    private readonly TimeProvider time;
    private readonly FolderAccess access;

    /// <summary>Creates the service for one unit of work.</summary>
    public FolderGrantService(AutoCanvasDbContext database, IAuditLog audit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        this.audit = audit;
        this.time = time;
        access = new FolderAccess(database);
    }

    /// <summary>The grants made directly on the folder, users first, then groups, by name.</summary>
    public async Task<UseCaseResult<IReadOnlyList<GrantView>>> ListAsync(Actor actor, Guid folderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var folder = await access.RequireAsync(actor, folderId, FolderRole.Owner, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<IReadOnlyList<GrantView>>(denied);
        }

        var grants = await database.FolderGrants.AsNoTracking()
            .Where(g => g.FolderId == folderId)
            .Select(g => new
            {
                g.Id,
                g.UserId,
                g.GroupId,
                UserName = g.User == null ? null : g.User.UserName,
                GroupName = g.Group == null ? null : g.Group.Name,
                g.Role,
                g.CreatedAt,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success<IReadOnlyList<GrantView>>(
        [
            .. grants
                .Select(g => g.UserId is { } userId
                    ? new GrantView(g.Id, "user", userId, g.UserName!, g.Role, g.CreatedAt)
                    : new GrantView(g.Id, "group", g.GroupId!.Value, g.GroupName!, g.Role, g.CreatedAt))
                .OrderBy(g => g.SubjectType, StringComparer.Ordinal)
                .ThenBy(g => g.SubjectName, StringComparer.OrdinalIgnoreCase),
        ]);
    }

    /// <summary>Gives a user or a group a role on the folder, or changes the role they have there.</summary>
    public async Task<UseCaseResult<GrantView>> SetAsync(Actor actor, Guid folderId, Guid? userId, Guid? groupId, FolderRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if ((userId is null) == (groupId is null))
        {
            return UseCase.Failure<GrantView>(UseCaseError.Invalid("userId", "Give either a userId or a groupId."));
        }

        if (role is < FolderRole.Viewer or > FolderRole.Owner)
        {
            return UseCase.Failure<GrantView>(UseCaseError.Invalid("role", "The role must be Viewer, Operator, Editor or Owner."));
        }

        var folder = await access.RequireAsync(actor, folderId, FolderRole.Owner, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<GrantView>(denied);
        }

        GrantSubject? subject = userId is { } user
            ? await database.Users.Where(u => u.Id == user).Select(u => GrantSubject.User(u.Id, u.UserName)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : await database.Groups.Where(g => g.Id == groupId).Select(g => GrantSubject.Group(g.Id, g.Name)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (subject is null)
        {
            return UseCase.Failure<GrantView>(UseCaseError.NotFound($"The {(userId is null ? "group" : "user")} does not exist."));
        }

        await FolderGrantWriter.SetAsync(database, audit, folderId, subject, role, time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var grant = await database.FolderGrants.AsNoTracking()
            .FirstAsync(g => g.FolderId == folderId && g.UserId == subject.UserId && g.GroupId == subject.GroupId, cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new GrantView(grant.Id, subject.Type, subject.Id, subject.Name, grant.Role, grant.CreatedAt));
    }

    /// <summary>Removes a grant. Owners may remove their own; an admin can always repair access.</summary>
    public async Task<UseCaseResult<Done>> RemoveAsync(Actor actor, Guid folderId, Guid grantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var folder = await access.RequireAsync(actor, folderId, FolderRole.Owner, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<Done>(denied);
        }

        var grant = await database.FolderGrants.Include(g => g.User).Include(g => g.Group)
            .FirstOrDefaultAsync(g => g.Id == grantId && g.FolderId == folderId, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound("The grant does not exist."));
        }

        var subject = grant.UserId is { } userId ? GrantSubject.User(userId, grant.User!.UserName) : GrantSubject.Group(grant.GroupId!.Value, grant.Group!.Name);
        database.FolderGrants.Remove(grant);
        audit.Record(FolderGrantWriter.Entry(AuditActions.FolderGrantRemoved, folderId, subject, grant.Role));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }
}
