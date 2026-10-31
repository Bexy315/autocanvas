namespace AutoCanvas.Persistence.Queue;

/// <summary>
/// Hands queued attempts to runners, so that each attempt goes to exactly one runner even when many
/// ask at once (ADR-0010, ADR-0015). Each database provider implements it its own way.
/// </summary>
public interface IJobClaimer
{
    /// <summary>
    /// Offers the oldest queued attempt the runner can take to it and returns its id, or
    /// <see langword="null"/> when there is none. The change is saved before this returns.
    /// </summary>
    Task<Guid?> ClaimAsync(AutoCanvasDbContext database, JobClaim claim, CancellationToken cancellationToken);
}
