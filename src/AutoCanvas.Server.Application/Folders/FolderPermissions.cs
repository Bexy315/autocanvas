using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>
/// The permission rule of ADR-0012 as a pure function: what role does a subject have on a folder,
/// given the folders above it and the subject's grants?
/// </summary>
public static class FolderPermissions
{
    /// <summary>
    /// The effective role on the last folder of <paramref name="chain"/>. Admins are owners
    /// everywhere. Otherwise the chain is cut at the nearest restricted folder (the folder itself
    /// included), and the highest role granted from there downwards wins.
    /// </summary>
    /// <param name="chain">Folders from the top level down to the folder in question.</param>
    /// <param name="grants">The subject's grants (own and through groups) on any folders.</param>
    /// <param name="isAdmin">Whether the subject is an instance admin.</param>
    public static FolderRole EffectiveRole(IReadOnlyList<FolderChainEntry> chain, IReadOnlyCollection<FolderGrantFact> grants, bool isAdmin)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(grants);
        if (chain.Count == 0)
        {
            return FolderRole.None;
        }

        if (isAdmin)
        {
            return FolderRole.Owner;
        }

        var start = 0;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].IsRestricted)
            {
                start = i;
                break;
            }
        }

        var role = FolderRole.None;
        for (var i = start; i < chain.Count; i++)
        {
            foreach (var grant in grants)
            {
                if (grant.FolderId == chain[i].Id && grant.Role > role)
                {
                    role = grant.Role;
                }
            }
        }

        return role;
    }
}
