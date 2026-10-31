using System.Collections.Concurrent;
using AutoCanvas.Core;
using AutoCanvas.Core.Model;
using AutoCanvas.Persistence.Runs;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Parsed automation files of runs, kept per server. A run's copy of its file never changes, so the
/// run's id is a safe key; parsing it again for every step and heartbeat would cost far more than
/// the planning itself (an S0 carry-over).
/// </summary>
internal static class RunDefinitions
{
    /// <summary>
    /// How many definitions are kept. When there are more, the cache starts over: active runs are
    /// parsed again on their next use, which is cheap enough to need no eviction order.
    /// </summary>
    public const int Capacity = 500;

    private static readonly ConcurrentDictionary<Guid, AutomationDefinition> Parsed = new();

    public static AutomationDefinition Get(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (Parsed.TryGetValue(run.Id, out var cached))
        {
            return cached;
        }

        var definition = AutomationLoader.Load(run.Yaml).Definition
            ?? throw new InvalidOperationException($"Run {run.Id} carries an automation file that does not load; only valid files are saved.");
        if (Parsed.Count >= Capacity)
        {
            Parsed.Clear();
        }

        Parsed[run.Id] = definition;
        return definition;
    }
}
