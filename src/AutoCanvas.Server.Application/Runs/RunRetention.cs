using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Deletes finished runs older than <see cref="RunOptions.RetentionDays"/>, except the newest
/// <see cref="RunOptions.KeepPerAutomation"/> of each automation (ADR-0015). Jobs, attempts, steps,
/// logs and reports go with their run through the database's cascades; the audit log records the cut.
/// </summary>
public sealed class RunRetention(AutoCanvasDbContext database, IAuditLog audit, IOptions<RunOptions> options, TimeProvider time)
{
    /// <summary>Runs deleted per statement, so a first run on a large database does not hold one huge transaction.</summary>
    internal const int BatchSize = 500;

    /// <summary>Deletes what is too old and returns how many runs that were; nothing when retention is off.</summary>
    public async Task<int> PruneAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        if (settings.RetentionDays <= 0)
        {
            return 0;
        }

        var before = time.GetUtcNow().AddDays(-settings.RetentionDays);
        var old = database.Runs.Where(r => r.State != RunState.Running && r.FinishedAt < before);
        var automations = await old.Where(r => r.AutomationId != null).Select(r => r.AutomationId!.Value).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await DeleteAsync(old.Where(r => r.AutomationId == null), cancellationToken).ConfigureAwait(false);
        foreach (var automation in automations)
        {
            // The newest runs stay, by number: the one at this place and everything after it.
            var keepFrom = settings.KeepPerAutomation == 0
                ? int.MaxValue
                : await database.Runs.Where(r => r.AutomationId == automation)
                    .OrderByDescending(r => r.Number)
                    .Skip(settings.KeepPerAutomation - 1)
                    .Select(r => (int?)r.Number)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? 0;
            deleted += await DeleteAsync(old.Where(r => r.AutomationId == automation && r.Number < keepFrom), cancellationToken).ConfigureAwait(false);
        }

        if (deleted > 0)
        {
            audit.Record(new AuditEntry(AuditActions.RunsPruned) { Details = AuditEntry.DetailsOf(("before", before), ("deleted", deleted)) });
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private async Task<int> DeleteAsync(IQueryable<Run> runs, CancellationToken cancellationToken)
    {
        var deleted = 0;
        while (true)
        {
            var batch = await runs.OrderBy(r => r.Id).Select(r => r.Id).Take(BatchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return deleted;
            }

            deleted += await database.Runs.Where(r => batch.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
