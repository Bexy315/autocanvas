namespace AutoCanvas.Core.Orchestration;

/// <summary>The state of a run, as far as decisions about it need it.</summary>
/// <param name="Jobs">The state of every job, by job id. Jobs missing here count as pending.</param>
/// <param name="CancelRequested">Whether someone asked to cancel the run.</param>
public sealed record RunState(IReadOnlyDictionary<string, JobState> Jobs, bool CancelRequested = false)
{
    /// <summary>The state of job <paramref name="id"/>; pending if it has none yet.</summary>
    /// <param name="id">Job id.</param>
    /// <returns>The job's state.</returns>
    public JobState Of(string id) => Jobs.GetValueOrDefault(id) ?? JobState.Pending;
}
