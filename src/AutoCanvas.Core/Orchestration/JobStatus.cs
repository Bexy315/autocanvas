namespace AutoCanvas.Core.Orchestration;

/// <summary>Where a job of a run stands.</summary>
public enum JobStatus
{
    /// <summary>Not started yet; waiting for its needs.</summary>
    Pending,

    /// <summary>Handed to the queue or running on a runner.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Succeeded,

    /// <summary>Finished with a failure.</summary>
    Failed,

    /// <summary>Stopped because the run was cancelled.</summary>
    Cancelled,

    /// <summary>Not run, because a need or condition ruled it out.</summary>
    Skipped,
}
