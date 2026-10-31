using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Variables;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>
/// Decides who may use variables and secrets of a scope. Folders and automations go by the folder's
/// role (Viewer lists, Operator reads variable values, Editor changes). The instance is readable by
/// everyone signed in, since its values reach every run anyway, and changed only by admins.
/// </summary>
internal sealed class ScopeAccess(AutoCanvasDbContext database)
{
    private readonly FolderAccess folders = new(database);

    public static UseCaseError AutomationNotFound => UseCaseError.NotFound("The automation does not exist, or you may not see it.");

    public async Task<UseCaseResult<ResolvedScope>> RequireAsync(Actor actor, ConfigurationScope scope, FolderRole needed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        switch (scope.Kind)
        {
            case ConfigurationScopeKind.Instance:
                var role = actor.IsAdmin ? FolderRole.Owner : FolderRole.Operator;
                return role >= needed
                    ? UseCase.Success(new ResolvedScope(ValueScopes.Instance, null, null, null, role))
                    : UseCase.Failure<ResolvedScope>(UseCaseError.Forbidden("Only admins change instance variables and secrets."));

            case ConfigurationScopeKind.Folder:
                var folderId = scope.Id.GetValueOrDefault();
                var folder = await folders.RequireAsync(actor, folderId, needed, track: false, cancellationToken).ConfigureAwait(false);
                return folder.Error is { } denied
                    ? UseCase.Failure<ResolvedScope>(denied)
                    : UseCase.Success(new ResolvedScope(ValueScopes.Folder(folderId), folderId, null, folderId, folder.Value!.Role));

            default:
                var automationId = scope.Id.GetValueOrDefault();
                var home = await database.Automations.AsNoTracking()
                    .Where(a => a.Id == automationId)
                    .Select(a => (Guid?)a.FolderId)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (home is not { } homeId)
                {
                    return UseCase.Failure<ResolvedScope>(AutomationNotFound);
                }

                var access = await folders.RequireAsync(actor, homeId, needed, track: false, cancellationToken).ConfigureAwait(false);
                if (access.Error is { } error)
                {
                    return UseCase.Failure<ResolvedScope>(error.Kind == UseCaseErrorKind.NotFound ? AutomationNotFound : error);
                }

                return UseCase.Success(new ResolvedScope(ValueScopes.Automation(automationId), null, automationId, homeId, access.Value!.Role));
        }
    }
}
