using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Persistence.Postgres;

/// <summary>
/// Claims with <c>FOR UPDATE SKIP LOCKED</c> (ADR-0010): concurrent claims, also from several
/// servers, lock different rows instead of waiting for each other, so every attempt is offered once.
/// </summary>
public sealed class PostgresJobClaimer : IJobClaimer
{
    /// <inheritdoc/>
    public async Task<Guid?> ClaimAsync(AutoCanvasDbContext database, JobClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(claim);
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // The locks on the candidates last until the commit; the ones not taken are free again then.
            var candidates = await database.JobAttempts
                .FromSql($"""
                    SELECT * FROM job_attempts
                    WHERE status = 'Queued' AND not_before <= {claim.Now}
                    ORDER BY queued_at, id
                    LIMIT {JobRequirements.CandidateCount}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var attempt = candidates.FirstOrDefault(a => JobRequirements.Fits(a, claim));
            if (attempt is not null)
            {
                JobRequirements.Offer(attempt, claim);
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return attempt?.Id;
        }
    }
}
