namespace AutoCanvas.Persistence.Runs;

/// <summary>
/// One step of a job attempt, from when the server decided about it. The steps of an attempt are
/// its progress: any server can continue the attempt from them after the runner reconnects.
/// </summary>
public sealed class JobStep
{
    /// <summary>Column size of <see cref="StepId"/>.</summary>
    public const int MaxStepIdLength = 100;

    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Column size of <see cref="Reason"/> and <see cref="Error"/>.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>Column size of <see cref="Outputs"/>.</summary>
    public const int MaxOutputsLength = 65536;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The attempt.</summary>
    public required Guid AttemptId { get; init; }

    /// <summary>Position of the step in the job, from 0; the protocol's <c>step_index</c>.</summary>
    public required int Index { get; init; }

    /// <summary>The step's <c>id</c> in the automation, if it has one.</summary>
    public string? StepId { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Where the step stands.</summary>
    public JobStepStatus Status { get; set; }

    /// <summary>Why it was skipped.</summary>
    public string? Reason { get; init; }

    /// <summary>Exit code of a script step, if it exited.</summary>
    public int? ExitCode { get; set; }

    /// <summary>The step's outputs as a JSON object of strings, masked.</summary>
    public string? Outputs { get; set; }

    /// <summary>What went wrong, when the step did not succeed; masked.</summary>
    public string? Error { get; set; }

    /// <summary>When it started, by the runner's clock.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it ended, by the runner's clock.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Navigation to the attempt.</summary>
    public JobAttempt? Attempt { get; init; }
}
