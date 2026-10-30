namespace AutoCanvas.Core.Orchestration;

/// <summary>The state of one job in a run.</summary>
/// <param name="Status">Where the job stands.</param>
/// <param name="ActivatedCase">For a finished switch: the case it activated, or <see langword="null"/> if none.</param>
/// <param name="Attempt">The current attempt, starting at 1.</param>
public sealed record JobState(JobStatus Status, string? ActivatedCase = null, int Attempt = 1)
{
    /// <summary>A job that has not started.</summary>
    public static JobState Pending { get; } = new(JobStatus.Pending);

    /// <summary>Whether the job will not change any more.</summary>
    public bool IsFinished => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped;
}
