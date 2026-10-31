using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Persistence.Sqlite;

/// <summary>
/// Claims one at a time inside the one server process a SQLite installation has (ADR-0010). The
/// offer is still made only if the attempt is still queued, so a claim never takes an attempt twice.
/// </summary>
public sealed class SqliteJobClaimer : IJobClaimer, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <inheritdoc/>
    public async Task<Guid?> ClaimAsync(AutoCanvasDbContext database, JobClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(claim);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var candidates = await database.JobAttempts.AsNoTracking()
                .Where(a => a.Status == AttemptStatus.Queued && a.NotBefore <= claim.Now)
                .OrderBy(a => a.QueuedAt).ThenBy(a => a.Id)
                .Take(JobRequirements.CandidateCount)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var attempt in candidates.Where(a => JobRequirements.Fits(a, claim)))
            {
                var offered = await database.JobAttempts
                    .Where(a => a.Id == attempt.Id && a.Status == AttemptStatus.Queued)
                    .ExecuteUpdateAsync(
                        set => set
                            .SetProperty(a => a.Status, AttemptStatus.Offered)
                            .SetProperty(a => a.RunnerId, claim.RunnerId)
                            .SetProperty(a => a.LeaseExpiresAt, claim.LeaseUntil),
                        cancellationToken).ConfigureAwait(false);
                if (offered == 1)
                {
                    return attempt.Id;
                }
            }

            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => gate.Dispose();
}
