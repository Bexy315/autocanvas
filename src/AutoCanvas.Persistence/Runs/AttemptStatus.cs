namespace AutoCanvas.Persistence.Runs;

/// <summary>Where an attempt stands.</summary>
public enum AttemptStatus
{
    /// <summary>Waiting for a runner.</summary>
    Queued,

    /// <summary>Offered to a runner, which has not accepted it yet; returns to the queue if it does not.</summary>
    Offered,

    /// <summary>A runner executes it.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Succeeded,

    /// <summary>Finished with a failure.</summary>
    Failed,

    /// <summary>Cancelled.</summary>
    Cancelled,
}
