using System.Text.Json;
using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Persistence.Queue;

/// <summary>Whether a runner fits a queued attempt; shared by every <see cref="IJobClaimer"/>.</summary>
public static class JobRequirements
{
    /// <summary>How many of the oldest waiting attempts a claim looks at.</summary>
    public const int CandidateCount = 50;

    /// <summary>A list of names as stored in <see cref="JobAttempt.Labels"/> and <see cref="JobAttempt.NodeTypes"/>.</summary>
    public static string Write(IEnumerable<string> names) => JsonSerializer.Serialize(names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    /// <summary>Whether the runner has every label, every node type and the executor the attempt needs.</summary>
    public static bool Fits(JobAttempt attempt, JobClaim claim)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(claim);
        return (claim.RunsContainers || !attempt.NeedsContainer)
            && Read(attempt.Labels).All(claim.Labels.Contains)
            && Read(attempt.NodeTypes).All(claim.NodeTypes.Contains);
    }

    /// <summary>Marks the attempt as offered to the runner.</summary>
    public static void Offer(JobAttempt attempt, JobClaim claim)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(claim);
        attempt.Status = AttemptStatus.Offered;
        attempt.RunnerId = claim.RunnerId;
        attempt.LeaseExpiresAt = claim.LeaseUntil;
    }

    // Attempts queued before requirements were stored have none.
    private static string[] Read(string names) => string.IsNullOrEmpty(names) ? [] : JsonSerializer.Deserialize<string[]>(names) ?? [];
}
