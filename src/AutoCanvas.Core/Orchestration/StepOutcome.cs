namespace AutoCanvas.Core.Orchestration;

/// <summary>How a step ended, as reported by the runner (runner protocol <c>StepOutcome</c>).</summary>
public enum StepOutcome
{
    /// <summary>Completed successfully.</summary>
    Succeeded,

    /// <summary>The step itself failed.</summary>
    Failed,

    /// <summary>Stopped because the job was cancelled.</summary>
    Cancelled,

    /// <summary>Exceeded its timeout.</summary>
    TimedOut,

    /// <summary>The runner could not execute it.</summary>
    Errored,

    /// <summary>Not run: its condition was false, or an earlier step failed.</summary>
    Skipped,
}
