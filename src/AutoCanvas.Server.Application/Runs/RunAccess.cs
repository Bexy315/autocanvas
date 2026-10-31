using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Whether a caller may read what happened in a run: Viewer on the run's folder (ADR-0012). A run
/// the caller may not see does not exist for them.
/// </summary>
internal sealed class RunAccess(AutoCanvasDbContext database)
{
    private readonly FolderAccess access = new(database);

    public static UseCaseError NotFound => UseCaseError.NotFound("The run does not exist.");

    /// <summary><see langword="null"/> when the caller may read the run, else why not.</summary>
    public async Task<UseCaseError?> ViewAsync(Actor actor, Guid runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var folder = await database.Runs.AsNoTracking().Where(r => r.Id == runId).Select(r => (Guid?)r.FolderId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            return NotFound;
        }

        var allowed = await access.RequireAsync(actor, folder.Value, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        return allowed.Error is { } error ? (error.Kind == UseCaseErrorKind.NotFound ? NotFound : error) : null;
    }
}
