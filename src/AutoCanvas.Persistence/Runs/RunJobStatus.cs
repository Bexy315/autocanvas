namespace AutoCanvas.Persistence.Runs;

/// <summary>A job's status; the same values as the job status of the automation engine.</summary>
public enum RunJobStatus
{
    /// <summary>Not started yet.</summary>
    Pending,

    /// <summary>Started: queued for a runner or executing.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Succeeded,

    /// <summary>Finished with a failure.</summary>
    Failed,

    /// <summary>Cancelled before or while it ran.</summary>
    Cancelled,

    /// <summary>Not run, because of a condition or a need.</summary>
    Skipped,
}
