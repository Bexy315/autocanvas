using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>
/// Loads what <see cref="FolderPermissions"/> needs (the folder chain and the actor's grants on
/// it) and answers "may this actor do that here?" for the folder use cases of one request.
/// </summary>
internal sealed class FolderAccess(AutoCanvasDbContext database)
{
    private List<Guid>? groupIds;

    /// <summary>
    /// The folder if the actor has at least <paramref name="needed"/> on it. A folder the actor
    /// cannot even view is reported as not found, so its existence stays hidden.
    /// </summary>
    public async Task<UseCaseResult<AccessibleFolder>> RequireAsync(Actor actor, Guid folderId, FolderRole needed, bool track, CancellationToken cancellationToken)
    {
        var folders = track ? database.Folders : database.Folders.AsNoTracking();
        var folder = await folders.FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            return UseCase.Failure<AccessibleFolder>(FolderNotFound);
        }

        var accessible = await EvaluateAsync(actor, folder, cancellationToken).ConfigureAwait(false);
        if (accessible.Role == FolderRole.None)
        {
            return UseCase.Failure<AccessibleFolder>(FolderNotFound);
        }

        return accessible.Role < needed
            ? UseCase.Failure<AccessibleFolder>(UseCaseError.Forbidden($"This needs the {needed} role on the folder; you have {accessible.Role}."))
            : UseCase.Success(accessible);
    }

    /// <summary>The actor's role on a folder, with chain and grants.</summary>
    public async Task<AccessibleFolder> EvaluateAsync(Actor actor, Folder folder, CancellationToken cancellationToken)
    {
        var chain = await ChainAsync(folder.Path, cancellationToken).ConfigureAwait(false);
        var grants = await GrantsAsync(actor, [.. chain.Select(c => c.Id)], cancellationToken).ConfigureAwait(false);
        return new AccessibleFolder(folder, FolderPermissions.EffectiveRole(chain, grants, actor.IsAdmin), chain, grants);
    }

    /// <summary>The folders from the top level down to the one with this path, in order.</summary>
    public async Task<IReadOnlyList<FolderChainEntry>> ChainAsync(string path, CancellationToken cancellationToken)
    {
        var ids = FolderPaths.Ids(path);
        var entries = await database.Folders.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new FolderChainEntry(f.Id, f.Name, f.IsRestricted))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. entries.OrderBy(e => IndexOf(ids, e.Id))];
    }

    /// <summary>The actor's grants (own and through groups) on the given folders; none for admins, who need none.</summary>
    public async Task<IReadOnlyCollection<FolderGrantFact>> GrantsAsync(Actor actor, IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken)
    {
        if (actor.IsAdmin || folderIds.Count == 0)
        {
            return [];
        }

        var groups = await GroupIdsAsync(actor, cancellationToken).ConfigureAwait(false);
        var ids = folderIds.ToList();
        return await database.FolderGrants.AsNoTracking()
            .Where(g => ids.Contains(g.FolderId) && (g.UserId == actor.UserId || (g.GroupId != null && groups.Contains(g.GroupId.Value))))
            .Select(g => new FolderGrantFact(g.FolderId, g.Role))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every grant the actor holds anywhere, own and through groups.</summary>
    public async Task<IReadOnlyCollection<FolderGrantFact>> AllGrantsAsync(Actor actor, CancellationToken cancellationToken)
    {
        var groups = await GroupIdsAsync(actor, cancellationToken).ConfigureAwait(false);
        return await database.FolderGrants.AsNoTracking()
            .Where(g => g.UserId == actor.UserId || (g.GroupId != null && groups.Contains(g.GroupId.Value)))
            .Select(g => new FolderGrantFact(g.FolderId, g.Role))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The breadcrumb above a folder: names, and ids only where the actor may open them.</summary>
    public static IReadOnlyList<FolderCrumb> Crumbs(IReadOnlyList<FolderChainEntry> chain, IReadOnlyCollection<FolderGrantFact> grants, bool isAdmin)
    {
        var crumbs = new List<FolderCrumb>(chain.Count);
        for (var i = 0; i < chain.Count - 1; i++)
        {
            var role = FolderPermissions.EffectiveRole([.. chain.Take(i + 1)], grants, isAdmin);
            crumbs.Add(new FolderCrumb(role == FolderRole.None ? null : chain[i].Id, chain[i].Name));
        }

        return crumbs;
    }

    /// <summary>The folder as the actor sees it.</summary>
    public static FolderView View(AccessibleFolder accessible, bool isAdmin)
    {
        var folder = accessible.Folder;
        return new FolderView(
            folder.Id,
            folder.Name,
            folder.Description,
            folder.ParentId,
            folder.IsRestricted,
            Crumbs(accessible.Chain, accessible.Grants, isAdmin),
            accessible.Role,
            folder.CreatedAt,
            folder.Version);
    }

    public static UseCaseError FolderNotFound => UseCaseError.NotFound("The folder does not exist, or you may not see it.");

    private static int IndexOf(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return i;
            }
        }

        return ids.Count;
    }

    private async Task<List<Guid>> GroupIdsAsync(Actor actor, CancellationToken cancellationToken) =>
        groupIds ??= await database.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == actor.UserId)
            .Select(m => m.GroupId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
