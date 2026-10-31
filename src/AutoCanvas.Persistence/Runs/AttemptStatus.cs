namespace AutoCanvas.Persistence.Runs;

/// <summary>Where an attempt stands.</summary>
public enum AttemptStatus
{
    /// <summary>Waiting for a runner.</summary>
    Queued,

    /// <summary>A runner executes it.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Succeeded,

    /// <summary>Finished with a failure.</summary>
    Failed,

    /// <summary>Cancelled.</summary>
    Cancelled,
}
