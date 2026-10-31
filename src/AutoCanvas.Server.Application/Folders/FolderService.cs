using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>
/// The folder tree: creating, reading, listing, renaming, moving, restricting and deleting folders,
/// each checked against the actor's role (ADR-0012).
/// </summary>
public sealed class FolderService
{
    private readonly AutoCanvasDbContext database;
    private readonly IAuditLog audit;
    private readonly TimeProvider time;
    private readonly FolderAccess access;

    /// <summary>Creates the service for one unit of work.</summary>
    public FolderService(AutoCanvasDbContext database, IAuditLog audit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        this.audit = audit;
        this.time = time;
        access = new FolderAccess(database);
    }

    /// <summary>
    /// Creates a folder. Anyone may create one at the top level and becomes its owner; a subfolder
    /// needs the Editor role on its parent and inherits from there.
    /// </summary>
    public async Task<UseCaseResult<FolderView>> CreateAsync(Actor actor, NewFolder request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        if ((Names.Check("name", request.Name, Folder.MaxNameLength, "folder") ?? CheckDescription(request.Description)) is { } invalid)
        {
            return UseCase.Failure<FolderView>(invalid);
        }

        string? parentPath = null;
        if (request.ParentId is { } parentId)
        {
            var parent = await access.RequireAsync(actor, parentId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
            if (parent.Error is { } denied)
            {
                return UseCase.Failure<FolderView>(denied);
            }

            parentPath = parent.Value!.Folder.Path;
            if (FolderPaths.Depth(parentPath) >= Folder.MaxDepth)
            {
                return UseCase.Failure<FolderView>(UseCaseError.Invalid("parentId", $"Folders can be nested at most {Folder.MaxDepth} levels deep."));
            }
        }

        var name = request.Name.Trim();
        if (await NameTakenAsync(request.ParentId, name, exceptId: null, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<FolderView>(NameTaken(name));
        }

        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var folder = new Folder
        {
            Id = id,
            ParentId = request.ParentId,
            ParentKey = request.ParentId ?? Guid.Empty,
            Name = name,
            NormalizedName = IdentityNames.Normalize(name),
            Path = FolderPaths.Child(parentPath, id),
            Description = NullIfEmpty(request.Description),
            CreatedAt = now,
            Version = 1,
        };
        database.Folders.Add(folder);
        audit.Record(Entry(AuditActions.FolderCreated, folder, ("name", folder.Name), ("parentId", folder.ParentId)));
        if (request.ParentId is null && !actor.IsAdmin)
        {
            await FolderGrantWriter.SetAsync(database, audit, folder.Id, await ActorSubjectAsync(actor, cancellationToken).ConfigureAwait(false), FolderRole.Owner, now, cancellationToken).ConfigureAwait(false);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(actor, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One folder with the actor's role and the breadcrumb above it.</summary>
    public async Task<UseCaseResult<FolderView>> GetAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var accessible = await access.RequireAsync(actor, id, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        return accessible.Error is { } error
            ? UseCase.Failure<FolderView>(error)
            : UseCase.Success(FolderAccess.View(accessible.Value!, actor.IsAdmin));
    }

    /// <summary>
    /// Where the actor can start browsing: for admins the top-level folders, for everyone else the
    /// topmost folders they may view, each with its breadcrumb. Ordered by path.
    /// </summary>
    public async Task<IReadOnlyList<FolderView>> ListEntryPointsAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.IsAdmin)
        {
            var roots = await database.Folders.AsNoTracking().Where(f => f.ParentId == null).OrderBy(f => f.NormalizedName)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return [.. roots.Select(f => FolderAccess.View(new AccessibleFolder(f, FolderRole.Owner, [new FolderChainEntry(f.Id, f.Name, f.IsRestricted)], []), isAdmin: true))];
        }

        var grants = await access.AllGrantsAsync(actor, cancellationToken).ConfigureAwait(false);
        var grantedIds = grants.Select(g => g.FolderId).Distinct().ToList();
        var granted = await database.Folders.AsNoTracking().Where(f => grantedIds.Contains(f.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var chainIds = granted.SelectMany(f => FolderPaths.Ids(f.Path)).Distinct().ToList();
        var chainEntries = (await database.Folders.AsNoTracking()
                .Where(f => chainIds.Contains(f.Id))
                .Select(f => new FolderChainEntry(f.Id, f.Name, f.IsRestricted))
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(e => e.Id);

        var entries = new List<(string SortKey, FolderView View)>();
        foreach (var folder in granted)
        {
            var chain = FolderPaths.Ids(folder.Path).Where(chainEntries.ContainsKey).Select(id => chainEntries[id]).ToList();
            var role = FolderPermissions.EffectiveRole(chain, grants, isAdmin: false);
            var parentRole = chain.Count > 1 ? FolderPermissions.EffectiveRole(chain[..^1], grants, isAdmin: false) : FolderRole.None;
            if (role != FolderRole.None && parentRole == FolderRole.None)
            {
                var view = FolderAccess.View(new AccessibleFolder(folder, role, chain, grants), isAdmin: false);
                entries.Add((string.Join('/', chain.Select(c => IdentityNames.Normalize(c.Name))), view));
            }
        }

        return [.. entries.OrderBy(e => e.SortKey, StringComparer.Ordinal).Select(e => e.View)];
    }

    /// <summary>
    /// The subfolders the actor may see, ordered by name, one page at a time. Restricted subfolders
    /// appear only to those with a role on them.
    /// </summary>
    public async Task<UseCaseResult<Page<FolderSummary>>> ListChildrenAsync(Actor actor, Guid parentId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        var after = NameCursor.Decode(page.Cursor);
        if ((limit.Error ?? after.Error) is { } invalid)
        {
            return UseCase.Failure<Page<FolderSummary>>(invalid);
        }

        var parent = await access.RequireAsync(actor, parentId, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        if (parent.Error is { } denied)
        {
            return UseCase.Failure<Page<FolderSummary>>(denied);
        }

        var items = new List<FolderSummary>();
        var cursor = after.Value;
        string? next = null;
        while (true)
        {
            var query = database.Folders.AsNoTracking().Where(f => f.ParentId == parentId);
            if (cursor is { } last)
            {
                query = query.Where(f => f.NormalizedName.CompareTo(last) > 0);
            }

            var batch = await query.OrderBy(f => f.NormalizedName).Take(limit.Value + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
            var ownGrants = await access.GrantsAsync(actor, [.. batch.Select(f => f.Id)], cancellationToken).ConfigureAwait(false);
            foreach (var child in batch)
            {
                var role = ChildRole(actor, parent.Value!.Role, child, ownGrants);
                if (role == FolderRole.None)
                {
                    continue;
                }

                if (items.Count == limit.Value)
                {
                    next = NameCursor.Encode(IdentityNames.Normalize(items[^1].Name));
                    break;
                }

                items.Add(new FolderSummary(child.Id, child.Name, child.Description, child.IsRestricted, role));
            }

            if (next is not null || batch.Count <= limit.Value)
            {
                break;
            }

            cursor = batch[^1].NormalizedName;
        }

        return UseCase.Success(new Page<FolderSummary>(items, next));
    }

    /// <summary>Renames a folder or changes its description (Editor).</summary>
    public async Task<UseCaseResult<FolderView>> UpdateAsync(Actor actor, Guid id, FolderChanges changes, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(changes);
        if (((changes.Name is null ? null : Names.Check("name", changes.Name, Folder.MaxNameLength, "folder")) ?? CheckDescription(changes.Description)) is { } invalid)
        {
            return UseCase.Failure<FolderView>(invalid);
        }

        var accessible = await RequireCurrentAsync(actor, id, FolderRole.Editor, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (accessible.Error is { } denied)
        {
            return UseCase.Failure<FolderView>(denied);
        }

        var folder = accessible.Value!.Folder;
        var changed = new List<string>();
        if (changes.Name?.Trim() is { } name && !string.Equals(name, folder.Name, StringComparison.Ordinal))
        {
            if (await NameTakenAsync(folder.ParentId, name, folder.Id, cancellationToken).ConfigureAwait(false))
            {
                return UseCase.Failure<FolderView>(NameTaken(name));
            }

            folder.Name = name;
            folder.NormalizedName = IdentityNames.Normalize(name);
            changed.Add("name");
        }

        if (changes.Description is { } description && !string.Equals(NullIfEmpty(description), folder.Description, StringComparison.Ordinal))
        {
            folder.Description = NullIfEmpty(description);
            changed.Add("description");
        }

        if (changed.Count > 0)
        {
            folder.Version++;
            audit.Record(Entry(AuditActions.FolderUpdated, folder, ("changed", changed), ("name", folder.Name)));
            if (await SaveAsync(cancellationToken).ConfigureAwait(false) is { } stale)
            {
                return UseCase.Failure<FolderView>(stale);
            }
        }

        return await GetAsync(actor, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a folder with everything below it. Needs Editor on the folder and on the new parent,
    /// or Owner to move it to the top level. An actor who would lose access through the move keeps
    /// their role by an explicit grant.
    /// </summary>
    public async Task<UseCaseResult<FolderView>> MoveAsync(Actor actor, Guid id, Guid? newParentId, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var accessible = await RequireCurrentAsync(actor, id, newParentId is null ? FolderRole.Owner : FolderRole.Editor, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (accessible.Error is { } denied)
        {
            return UseCase.Failure<FolderView>(denied);
        }

        var folder = accessible.Value!.Folder;
        if (folder.ParentId == newParentId)
        {
            return await GetAsync(actor, id, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<FolderChainEntry> targetChain = [];
        string? targetPath = null;
        if (newParentId is { } parentId)
        {
            var target = await access.RequireAsync(actor, parentId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
            if (target.Error is { } targetDenied)
            {
                return UseCase.Failure<FolderView>(targetDenied);
            }

            targetPath = target.Value!.Folder.Path;
            targetChain = target.Value.Chain;
            if (targetPath.StartsWith(folder.Path, StringComparison.Ordinal))
            {
                return UseCase.Failure<FolderView>(UseCaseError.Invalid("parentId", "A folder cannot be moved into itself or a folder below it."));
            }
        }

        if (await NameTakenAsync(newParentId, folder.Name, folder.Id, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<FolderView>(NameTaken(folder.Name));
        }

        var fromParentId = folder.ParentId;
        if (await RelocateAsync(folder, newParentId, targetPath, cancellationToken).ConfigureAwait(false) is { } tooDeep)
        {
            return UseCase.Failure<FolderView>(tooDeep);
        }

        audit.Record(Entry(AuditActions.FolderMoved, folder, ("fromParentId", fromParentId), ("toParentId", newParentId)));
        await KeepRoleAsync(actor, folder, accessible.Value.Role, [.. targetChain, new FolderChainEntry(folder.Id, folder.Name, folder.IsRestricted)], cancellationToken).ConfigureAwait(false);
        if (await SaveAsync(cancellationToken).ConfigureAwait(false) is { } stale)
        {
            return UseCase.Failure<FolderView>(stale);
        }

        return await GetAsync(actor, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a folder restricted, so grants from above stop applying, or lifts that (Owner). The
    /// actor keeps their access by an explicit Owner grant.
    /// </summary>
    public async Task<UseCaseResult<FolderView>> SetRestrictedAsync(Actor actor, Guid id, bool isRestricted, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var accessible = await RequireCurrentAsync(actor, id, FolderRole.Owner, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (accessible.Error is { } denied)
        {
            return UseCase.Failure<FolderView>(denied);
        }

        var folder = accessible.Value!.Folder;
        if (folder.IsRestricted != isRestricted)
        {
            folder.IsRestricted = isRestricted;
            folder.Version++;
            audit.Record(Entry(AuditActions.FolderRestrictionChanged, folder, ("isRestricted", isRestricted)));
            var chain = accessible.Value.Chain.Select(c => c.Id == folder.Id ? c with { IsRestricted = isRestricted } : c).ToList();
            await KeepRoleAsync(actor, folder, FolderRole.Owner, chain, cancellationToken).ConfigureAwait(false);
            if (await SaveAsync(cancellationToken).ConfigureAwait(false) is { } stale)
            {
                return UseCase.Failure<FolderView>(stale);
            }
        }

        return await GetAsync(actor, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes an empty folder with its grants (Owner).</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Actor actor, Guid id, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var accessible = await RequireCurrentAsync(actor, id, FolderRole.Owner, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (accessible.Error is { } denied)
        {
            return UseCase.Failure<Done>(denied);
        }

        var folder = accessible.Value!.Folder;
        if (await database.Folders.AnyAsync(f => f.ParentId == id, cancellationToken).ConfigureAwait(false)
            || await database.Automations.AnyAsync(a => a.FolderId == id, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<Done>(UseCaseError.Conflict("The folder is not empty. Move or delete its subfolders and automations first."));
        }

        database.Folders.Remove(folder);
        audit.Record(Entry(AuditActions.FolderDeleted, folder, ("name", folder.Name), ("parentId", folder.ParentId)));
        return await SaveAsync(cancellationToken).ConfigureAwait(false) is { } stale ? UseCase.Failure<Done>(stale) : Done.Result;
    }

    /// <summary>Hangs the folder and its subtree under the new parent, unless that nests too deep.</summary>
    private async Task<UseCaseError?> RelocateAsync(Folder folder, Guid? newParentId, string? targetPath, CancellationToken cancellationToken)
    {
        var oldPath = folder.Path;
        var descendants = await database.Folders.Where(f => f.Path.StartsWith(oldPath) && f.Id != folder.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var newPath = FolderPaths.Child(targetPath, folder.Id);
        var deepest = descendants.Select(d => FolderPaths.Depth(d.Path)).DefaultIfEmpty(FolderPaths.Depth(oldPath)).Max();
        if (deepest - FolderPaths.Depth(oldPath) + FolderPaths.Depth(newPath) > Folder.MaxDepth)
        {
            return UseCaseError.Invalid("parentId", $"Folders can be nested at most {Folder.MaxDepth} levels deep.");
        }

        folder.ParentId = newParentId;
        folder.ParentKey = newParentId ?? Guid.Empty;
        folder.Path = newPath;
        folder.Version++;
        foreach (var descendant in descendants)
        {
            descendant.Path = newPath + descendant.Path[oldPath.Length..];
        }

        return null;
    }

    private static FolderRole ChildRole(Actor actor, FolderRole parentRole, Folder child, IReadOnlyCollection<FolderGrantFact> grants)
    {
        if (actor.IsAdmin)
        {
            return FolderRole.Owner;
        }

        var role = child.IsRestricted ? FolderRole.None : parentRole;
        foreach (var grant in grants)
        {
            if (grant.FolderId == child.Id && grant.Role > role)
            {
                role = grant.Role;
            }
        }

        return role;
    }

    private static UseCaseError? CheckDescription(string? description) =>
        description is not null && description.Trim().Length > Folder.MaxDescriptionLength
            ? UseCaseError.Invalid("description", $"The description must have at most {Folder.MaxDescriptionLength} characters.")
            : null;

    private static UseCaseError NameTaken(string name) =>
        UseCaseError.Conflict($"There is a folder named '{name}' here already.", "name");

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditEntry Entry(string action, Folder folder, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "folder",
        TargetId = folder.Id.ToString(),
        FolderId = folder.Id,
        Details = AuditEntry.DetailsOf(details),
    };

    private async Task<UseCaseResult<AccessibleFolder>> RequireCurrentAsync(Actor actor, Guid id, FolderRole needed, int? expectedVersion, CancellationToken cancellationToken)
    {
        var accessible = await access.RequireAsync(actor, id, needed, track: true, cancellationToken).ConfigureAwait(false);
        return accessible.Error is null && expectedVersion is { } expected && expected != accessible.Value!.Folder.Version
            ? UseCase.Failure<AccessibleFolder>(UseCaseError.Stale())
            : accessible;
    }

    private async Task<bool> NameTakenAsync(Guid? parentId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var parentKey = parentId ?? Guid.Empty;
        var normalized = IdentityNames.Normalize(name);
        return await database.Folders.AnyAsync(f => f.ParentKey == parentKey && f.NormalizedName == normalized && f.Id != exceptId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GrantSubject> ActorSubjectAsync(Actor actor, CancellationToken cancellationToken)
    {
        var userName = await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstAsync(cancellationToken).ConfigureAwait(false);
        return GrantSubject.User(actor.UserId, userName);
    }

    /// <summary>Grants the actor <paramref name="role"/> explicitly if the new chain would give them less.</summary>
    private async Task KeepRoleAsync(Actor actor, Folder folder, FolderRole role, IReadOnlyList<FolderChainEntry> newChain, CancellationToken cancellationToken)
    {
        if (actor.IsAdmin)
        {
            return;
        }

        var grants = await access.GrantsAsync(actor, [.. newChain.Select(c => c.Id)], cancellationToken).ConfigureAwait(false);
        if (FolderPermissions.EffectiveRole(newChain, grants, isAdmin: false) < role)
        {
            await FolderGrantWriter.SetAsync(database, audit, folder.Id, await ActorSubjectAsync(actor, cancellationToken).ConfigureAwait(false), role, time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<UseCaseError?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return UseCaseError.Stale();
        }
    }
}
