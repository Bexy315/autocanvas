using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// The repositories a run may check out (ADR-0021): its own, for a repository automation, and those
/// connected in its folder and the folders above it, cut at a restricted folder like variables and
/// secrets are. Their credentials go to the job's runner, and the server masks them in its log.
/// </summary>
internal sealed class RepositoryCheckouts(AutoCanvasDbContext database, RepositoryCredentials credentials)
{
    /// <summary>The repository an automation comes from, or <see langword="null"/> for a managed one (or a deleted one).</summary>
    public async Task<Repository?> OfAutomationAsync(Guid? automationId, CancellationToken cancellationToken)
    {
        if (automationId is null)
        {
            return null;
        }

        var repositoryId = await database.Automations.AsNoTracking().Where(a => a.Id == automationId).Select(a => a.RepositoryId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return repositoryId is null ? null : await database.Repositories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The repository of that name in the folder or a folder above it, the nearest first.</summary>
    public async Task<Repository?> ByNameAsync(Guid folderId, string name, CancellationToken cancellationToken)
    {
        var normalized = IdentityNames.Normalize(name);
        var ids = await FolderIdsAsync(folderId, cancellationToken).ConfigureAwait(false);
        var found = await database.Repositories.AsNoTracking().Where(r => ids.Contains(r.FolderId) && r.NormalizedName == normalized).ToListAsync(cancellationToken).ConfigureAwait(false);
        return found.OrderBy(r => ids.IndexOf(r.FolderId)).FirstOrDefault();
    }

    /// <summary>The credentials of every repository the run's folder can check out, to mask them in its log.</summary>
    public async Task<IReadOnlyList<string>> SecretsAsync(Guid folderId, CancellationToken cancellationToken)
    {
        var ids = await FolderIdsAsync(folderId, cancellationToken).ConfigureAwait(false);
        var repositories = await database.Repositories.AsNoTracking()
            .Where(r => ids.Contains(r.FolderId) && (r.CredentialCiphertext != null || r.InstallationId != null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. repositories.SelectMany(credentials.Known)];
    }

    /// <summary>The repository as a checkout: its address, user name and credential.</summary>
    public async Task<OfferedCheckout> CheckoutAsync(Repository repository, string? gitRef, string? path, int depth, CancellationToken cancellationToken) =>
        new(repository.Url, gitRef, path, depth, RepositoryCredentials.UserNameOf(repository), await credentials.CheckoutSecretAsync(repository, cancellationToken).ConfigureAwait(false));

    /// <summary>The folder and the folders above it whose repositories apply, the nearest first.</summary>
    private async Task<List<Guid>> FolderIdsAsync(Guid folderId, CancellationToken cancellationToken)
    {
        var path = await database.Folders.AsNoTracking().Where(f => f.Id == folderId).Select(f => f.Path).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return [];
        }

        var chain = await new FolderAccess(database).ChainAsync(path, cancellationToken).ConfigureAwait(false);
        return [.. ConfigurationChain.ApplyingFolders(chain).Select(f => f.Id).Reverse()];
    }
}
