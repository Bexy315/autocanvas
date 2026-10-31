namespace AutoCanvas.Persistence.Runs;

/// <summary>Where a run stands; the same values as the run status of the automation engine.</summary>
public enum RunState
{
    /// <summary>Jobs are waiting or running.</summary>
    Running,

    /// <summary>Finished; every job that had to succeed did.</summary>
    Succeeded,

    /// <summary>Finished; a job failed.</summary>
    Failed,

    /// <summary>Finished after someone cancelled it.</summary>
    Cancelled,
}
