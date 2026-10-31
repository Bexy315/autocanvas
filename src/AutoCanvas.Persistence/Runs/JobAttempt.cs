namespace AutoCanvas.Persistence.Runs;

/// <summary>
/// One try to execute a job on a runner; its id is the runner protocol's <c>job_id</c>. Queued
/// attempts are what the job queue hands out (ADR-0015); it carries what a runner needs to take it.
/// </summary>
public sealed class JobAttempt
{
    /// <summary>Column size of <see cref="Labels"/> and <see cref="NodeTypes"/>.</summary>
    public const int MaxRequirementLength = 2000;

    /// <summary>Column size of <see cref="Error"/>.</summary>
    public const int MaxErrorLength = 500;

    /// <summary>Time-ordered identifier (UUID version 7); the protocol's job id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The job.</summary>
    public required Guid RunJobId { get; init; }

    /// <summary>1 for the first attempt, counting up with retries.</summary>
    public required int Number { get; init; }

    /// <summary>Where the attempt stands.</summary>
    public AttemptStatus Status { get; set; } = AttemptStatus.Queued;

    /// <summary>When it was queued.</summary>
    public required DateTimeOffset QueuedAt { get; init; }

    /// <summary>It is not handed out before this time (retry delays).</summary>
    public required DateTimeOffset NotBefore { get; init; }

    /// <summary>Labels a runner must all have (<c>runs_on</c>), as a JSON array.</summary>
    public required string Labels { get; init; }

    /// <summary>Node types a runner must support, as a JSON array.</summary>
    public required string NodeTypes { get; init; }

    /// <summary>Whether the job needs a container executor (an image or services).</summary>
    public required bool NeedsContainer { get; init; }

    /// <summary>The job's time limit once it runs.</summary>
    public required int TimeoutMinutes { get; init; }

    /// <summary>The runner that holds it while offered or running.</summary>
    public Guid? RunnerId { get; set; }

    /// <summary>The runner holds it until then unless it renews the lease with a heartbeat.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>Why it failed when the runner did not say: lost runner, time limit.</summary>
    public string? Error { get; set; }

    /// <summary>When a runner started it.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it ended.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Navigation to the job.</summary>
    public RunJob? Job { get; init; }
}
