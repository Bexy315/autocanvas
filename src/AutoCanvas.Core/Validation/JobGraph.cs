using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Validation;

/// <summary>
/// The jobs and their needs: checks that needs point to jobs that can be waited for, finds cycles,
/// and answers which jobs have finished before a given job starts.
/// </summary>
internal sealed class JobGraph
{
    private readonly Dictionary<string, JobDefinition> _jobs;
    private readonly Dictionary<string, HashSet<string>> _ancestors = new(StringComparer.Ordinal);

    public JobGraph(IReadOnlyList<JobDefinition> jobs)
    {
        _jobs = jobs.ToDictionary(j => j.Id, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> JobIds => _jobs.Keys;

    public JobDefinition? Find(string id) => _jobs.GetValueOrDefault(id);

    /// <summary>Reports needs that cannot be satisfied; returns false if the graph has a cycle.</summary>
    public bool Check(ValidationContext context)
    {
        foreach (var job in _jobs.Values)
        {
            CheckNeeds(job, context);
        }

        return !HasCycles(context);
    }

    /// <summary>Jobs that are certainly finished when <paramref name="id"/> starts: its needs, their needs, and so on.</summary>
    public IReadOnlySet<string> Ancestors(string id)
    {
        if (_ancestors.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([id]);
        while (pending.TryPop(out var current))
        {
            foreach (var need in Find(current)?.Needs ?? [])
            {
                if (result.Add(need.Job))
                {
                    pending.Push(need.Job);
                }
            }
        }

        _ancestors[id] = result;
        return result;
    }

    private void CheckNeeds(JobDefinition job, ValidationContext context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < job.Needs.Count; i++)
        {
            var need = job.Needs[i];
            var path = $"jobs.{job.Id}.needs[{i}]";
            if (!seen.Add(need.Job))
            {
                context.Error(DiagnosticCodes.DuplicateNeed, path, $"'{need.Job}' is listed twice in needs.");
                continue;
            }

            CheckNeed(need, path, context);
        }
    }

    private void CheckNeed(JobNeed need, string path, ValidationContext context)
    {
        var target = Find(need.Job);
        if (target is null)
        {
            context.Error(DiagnosticCodes.UnknownJob, path, $"There is no job '{need.Job}'.{Hint(need.Job)}");
            return;
        }

        if (target is StepJob { RunsFinally: true })
        {
            context.Error(DiagnosticCodes.NeedsFinallyJob, path, $"'{need.Job}' is a finally job and runs after every other job, so no job can wait for it.");
            return;
        }

        if (need.Case is not { } caseName)
        {
            return;
        }

        if (target is not SwitchJob switchJob)
        {
            context.Error(DiagnosticCodes.UnknownCase, path, $"'{need.Job}' is not a switch, so it has no case '{caseName}'.");
        }
        else if (!switchJob.Cases.Any(c => string.Equals(c.Name, caseName, StringComparison.Ordinal)))
        {
            context.Error(DiagnosticCodes.UnknownCase, path, $"The switch '{need.Job}' has no case '{caseName}'. Its cases are: {string.Join(", ", switchJob.Cases.Select(c => c.Name))}.");
        }
    }

    private string Hint(string id)
    {
        var closest = _jobs.Keys.MinBy(k => Parsing.Suggestions.Distance(id, k));
        return closest is not null && Parsing.Suggestions.Distance(id, closest) <= Math.Max(1, closest.Length / 3)
            ? $" Did you mean '{closest}'?"
            : string.Empty;
    }

    /// <summary>Depth-first search; reports each cycle once, at the job where it was found.</summary>
    private bool HasCycles(ValidationContext context)
    {
        var state = new Dictionary<string, bool>(StringComparer.Ordinal); // false: in progress, true: done
        var found = false;
        foreach (var id in _jobs.Keys)
        {
            found |= Visit(id, state, [], context);
        }

        return found;
    }

    private bool Visit(string id, Dictionary<string, bool> state, List<string> trail, ValidationContext context)
    {
        if (state.TryGetValue(id, out var done))
        {
            if (done)
            {
                return false;
            }

            var cycle = trail.Skip(trail.IndexOf(id)).Append(id);
            context.Error(DiagnosticCodes.DependencyCycle, $"jobs.{id}.needs", $"These jobs need each other in a cycle, so none of them can start: {string.Join(" → ", cycle)}.");
            return true;
        }

        state[id] = false;
        trail.Add(id);
        var found = false;
        foreach (var need in Find(id)?.Needs ?? [])
        {
            if (_jobs.ContainsKey(need.Job))
            {
                found |= Visit(need.Job, state, trail, context);
            }
        }

        trail.RemoveAt(trail.Count - 1);
        state[id] = true;
        return found;
    }
}
