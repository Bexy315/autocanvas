using System.Collections.Concurrent;
using AutoCanvas.Protocol.Runner.V1;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// The jobs the runner holds, and the <c>JobFinished</c> of jobs that ended a moment ago: after a
/// reconnect both are sent again, since the stream may have broken before they arrived.
/// </summary>
internal sealed class JobRegistry(TimeProvider time)
{
    /// <summary>How long a finished job's last message is sent again on reconnect.</summary>
    public static readonly TimeSpan FinishedMemory = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, JobExecution> active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (ConnectRequest Message, DateTimeOffset At)> finished = new(StringComparer.Ordinal);
    private TaskCompletionSource empty = Completed();

    public int Count => active.Count;

    public IReadOnlyList<string> Ids => [.. active.Keys];

    public IReadOnlyCollection<JobExecution> Jobs => [.. active.Values];

    public bool TryGet(string id, out JobExecution job) => active.TryGetValue(id, out job!);

    public bool TryAdd(JobExecution job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (active)
        {
            if (!active.TryAdd(job.Id, job))
            {
                return false;
            }

            if (empty.Task.IsCompleted)
            {
                empty = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return true;
        }
    }

    public void Finish(JobExecution job, ConnectRequest message)
    {
        ArgumentNullException.ThrowIfNull(job);
        finished[job.Id] = (message, time.GetUtcNow());
        lock (active)
        {
            active.TryRemove(job.Id, out _);
            if (active.IsEmpty)
            {
                empty.TrySetResult();
            }
        }
    }

    /// <summary>Whether the runner holds no job within <paramref name="wait"/>.</summary>
    public async Task<bool> WhenEmptyAsync(TimeSpan wait)
    {
        Task task;
        lock (active)
        {
            task = empty.Task;
        }

        return await Task.WhenAny(task, Task.Delay(wait)).ConfigureAwait(false) == task;
    }

    /// <summary>What to send again after a reconnect: each active job's last state, and recent ends.</summary>
    public List<ConnectRequest> Replay()
    {
        var cutoff = time.GetUtcNow() - FinishedMemory;
        foreach (var (id, entry) in finished)
        {
            if (entry.At < cutoff)
            {
                finished.TryRemove(id, out _);
            }
        }

        return [.. active.Values.Select(j => j.LastState).OfType<ConnectRequest>(), .. finished.Values.Select(f => f.Message)];
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
