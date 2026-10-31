using System.Text.Json;
using AutoCanvas.Core.Expressions;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// What a run's expressions read: <c>trigger.*</c>, <c>run.*</c>, <c>vars.*</c> and
/// <c>secrets.*</c>. Step outputs and <c>steps.*</c> come with the runner (S2 step 4).
/// </summary>
internal sealed class RunValues(Run run, ResolvedConfiguration configuration, string status) : IValueResolver
{
    public bool TryResolve(IReadOnlyList<string> path, out string? value)
    {
        ArgumentNullException.ThrowIfNull(path);
        value = null;
        return path switch
        {
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
        _ => "automation_completed",
    };

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
