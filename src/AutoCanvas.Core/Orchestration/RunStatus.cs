namespace AutoCanvas.Core.Orchestration;

/// <summary>How a run stands or ended.</summary>
public enum RunStatus
{
    /// <summary>Jobs are still pending or running.</summary>
    Running,

    /// <summary>All jobs that mattered succeeded.</summary>
    Succeeded,

    /// <summary>A job failed.</summary>
    Failed,

    /// <summary>The run was cancelled.</summary>
    Cancelled,
}
