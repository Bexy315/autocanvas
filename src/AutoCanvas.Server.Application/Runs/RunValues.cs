using System.Text.Json;
using AutoCanvas.Core.Expressions;
using AutoCanvas.Core.Model;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// What a run's expressions read: <c>trigger.*</c>, <c>run.*</c>, <c>vars.*</c>, <c>secrets.*</c>,
/// the outputs of finished jobs (<c>jobs.ID.outputs.NAME</c>) and, inside a job attempt, the outputs
/// of its earlier steps (<c>steps.ID.outputs.NAME</c>). <c>jobs.ID.result</c> comes from Core's
/// planners, which know the jobs' states.
/// </summary>
internal sealed class RunValues(
    Run run,
    ResolvedConfiguration configuration,
    string status,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? jobOutputs = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? stepOutputs = null) : IValueResolver
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> None =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>The run's secrets, for masking what comes back from runners.</summary>
    public IReadOnlyDictionary<string, string> Secrets => configuration.Secrets;

    /// <summary>The same values, inside a job attempt whose steps so far published these outputs.</summary>
    public RunValues WithSteps(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> steps) =>
        new(run, configuration, status, jobOutputs, steps);

    public bool TryResolve(IReadOnlyList<string> path, out string? value)
    {
        ArgumentNullException.ThrowIfNull(path);
        value = null;
        return path switch
        {
            ["jobs", var job, "outputs", var name] => Output(jobOutputs ?? None, job, name, out value),
            ["steps", var step, "outputs", var name] => Output(stepOutputs ?? None, step, name, out value),
            ["vars", var name] => configuration.Variables.TryGetValue(name, out value),
            ["secrets", var name] => configuration.Secrets.TryGetValue(name, out value),
            ["run", var field] => Run(field, out value),
            ["trigger", "event"] => Found(TriggerEvent(run.Trigger), out value),
            ["trigger", ..] => Trigger([.. path.Skip(1)], out value),
            _ => false,
        };
    }

    /// <summary>The value of <c>trigger.event</c>.</summary>
    public static string TriggerEvent(RunTriggerKind kind) => kind switch
    {
        RunTriggerKind.Manual => "manual",
        RunTriggerKind.Schedule => "schedule",
        RunTriggerKind.Webhook => "webhook",
        RunTriggerKind.Push => "push",
        _ => "automation_completed",
    };

    /// <summary>
    /// An output of a job or step. One that was not set reads as empty: what a script writes to its
    /// output file is only known at run time, so the file's references cannot all be checked on save.
    /// </summary>
    private static bool Output(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs, string owner, string name, out string? value)
    {
        value = outputs.TryGetValue(owner, out var published) && published.TryGetValue(name, out var found) ? found : null;
        return true;
    }

    private static bool Found(string? found, out string? value)
    {
        value = found;
        return true;
    }

    private bool Run(string field, out string? value) => field switch
    {
        "id" => Found(run.Id.ToString(), out value),
        "automation_name" => Found(run.AutomationName, out value),
        "triggered_by" => Found(run.TriggeredByName, out value),
        "branch" => Found(run.Ref is { } gitRef && gitRef.StartsWith(PushTrigger.BranchPrefix, StringComparison.Ordinal) ? gitRef[PushTrigger.BranchPrefix.Length..] : null, out value),
        "commit_sha" => Found(run.CommitSha, out value),
        "commit_short_sha" => Found(run.CommitSha?[..Math.Min(7, run.CommitSha.Length)], out value),
        "status" => Found(status, out value),
        _ => Found(null, out value) && false,
    };

    /// <summary>
    /// Reads the trigger's data: <c>trigger.branch</c> is the property <c>branch</c>,
    /// <c>trigger.headers.X</c> the property <c>X</c> of the object <c>headers</c>. Names of nested
    /// properties (headers, query) are compared regardless of case.
    /// </summary>
    private bool Trigger(IReadOnlyList<string> path, out string? value)
    {
        value = null;
        if (run.TriggerData is null || path.Count == 0)
        {
            return false;
        }

        using var document = JsonDocument.Parse(run.TriggerData);
        var element = document.RootElement;
        for (var i = 0; i < path.Count; i++)
        {
            if (element.ValueKind != JsonValueKind.Object || !TryProperty(element, path[i], ignoreCase: i > 0, out element))
            {
                return false;
            }
        }

        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
        return true;
    }

    private static bool TryProperty(JsonElement element, string name, bool ignoreCase, out JsonElement found)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                found = property.Value;
                return true;
            }
        }

        found = default;
        return false;
    }
}
