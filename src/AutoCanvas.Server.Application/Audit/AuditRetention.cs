using AutoCanvas.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Audit;

/// <summary>
/// Deletes audit entries older than <see cref="AuditOptions.RetentionDays"/> and records that it
/// did, so the log itself shows where it was cut.
/// </summary>
public sealed class AuditRetention(AutoCanvasDbContext database, IAuditLog audit, IOptions<AuditOptions> options, TimeProvider time)
{
    /// <summary>Entries deleted per statement, so a first run on a large log does not hold one huge transaction.</summary>
    internal const int BatchSize = 1000;

    /// <summary>Deletes what is too old and returns how many entries that were; nothing when retention is off.</summary>
    public async Task<int> PruneAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var days = options.Value.RetentionDays;
        if (days <= 0)
        {
            return 0;
        }

        var before = time.GetUtcNow().AddDays(-days);
        var deleted = 0;
        while (true)
        {
            var batch = await database.AuditEvents
                .Where(e => e.OccurredAt < before)
                .OrderBy(e => e.OccurredAt)
                .Select(e => e.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            deleted += await database.AuditEvents.Where(e => batch.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (deleted > 0)
        {
            audit.Record(new AuditEntry(AuditActions.AuditPruned) { Details = AuditEntry.DetailsOf(("before", before), ("deleted", deleted)) });
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }
}
