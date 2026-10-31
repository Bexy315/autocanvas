namespace AutoCanvas.Persistence.Runs;

/// <summary>A job of a run, as the automation engine sees it; each try to execute it is a <see cref="JobAttempt"/>.</summary>
public sealed class RunJob
{
    /// <summary>Column size of <see cref="Key"/>.</summary>
    public const int MaxKeyLength = 100;

    /// <summary>Column size of <see cref="Name"/> and <see cref="ActivatedCase"/>.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Column size of <see cref="Reason"/>.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The run.</summary>
    public required Guid RunId { get; init; }

    /// <summary>The job's id in the automation file, unique in the run.</summary>
    public required string Key { get; init; }

    /// <summary>Where the job stands in the automation file, from 0; lists keep that order.</summary>
    public required int Position { get; init; }

    /// <summary>The job's display name, or its key.</summary>
    public required string Name { get; init; }

    /// <summary>The job's status in the automation engine's terms.</summary>
    public RunJobStatus Status { get; set; } = RunJobStatus.Pending;

    /// <summary>For a switch job: the case it chose.</summary>
    public string? ActivatedCase { get; set; }

    /// <summary>Why the job was skipped, or what the switch decided.</summary>
    public string? Reason { get; set; }

    /// <summary>How many attempts it has had.</summary>
    public int AttemptCount { get; set; }

    /// <summary>When it started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it finished.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Navigation to the run.</summary>
    public Run? Run { get; init; }
}
