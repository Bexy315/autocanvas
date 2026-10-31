namespace AutoCanvas.Persistence.Runs;

/// <summary>Where a step of a job attempt stands.</summary>
public enum JobStepStatus
{
    /// <summary>Sent to the runner, which has not reported it started.</summary>
    Sent,

    /// <summary>The runner executes it.</summary>
    Running,

    /// <summary>Completed successfully.</summary>
    Succeeded,

    /// <summary>The step itself failed, such as a script exiting with another code than 0.</summary>
    Failed,

    /// <summary>Stopped because the attempt was cancelled.</summary>
    Cancelled,

    /// <summary>Exceeded its time limit.</summary>
    TimedOut,

    /// <summary>The runner could not execute it; not the step's fault.</summary>
    Errored,

    /// <summary>Never sent: its condition was false, or an earlier step failed.</summary>
    Skipped,
}
