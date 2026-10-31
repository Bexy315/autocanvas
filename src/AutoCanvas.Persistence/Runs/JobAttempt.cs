namespace AutoCanvas.Persistence.Runs;

/// <summary>
/// One try to execute a job on a runner; its id is the runner protocol's <c>job_id</c>. Queued
/// attempts are what the job queue hands out (ADR-0015).
/// </summary>
public sealed class JobAttempt
{
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

    /// <summary>When a runner started it.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it ended.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Navigation to the job.</summary>
    public RunJob? Job { get; init; }
}
