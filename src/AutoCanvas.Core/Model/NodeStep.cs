using System.Text.Json;

namespace AutoCanvas.Core.Model;

/// <summary>A step that executes a node type.</summary>
/// <param name="Id">Step id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Condition">The step's <c>if</c> expression.</param>
/// <param name="Env">Environment variables for this step.</param>
/// <param name="ContinueOnError">A failure of this step does not fail the job.</param>
/// <param name="TimeoutMinutes">Time limit for this step.</param>
/// <param name="Type">Node type, for example <c>http_request</c>.</param>
/// <param name="Inputs">The <c>with</c> block as a JSON object; an empty object when absent.</param>
/// <param name="Outputs">Extra outputs of the step, as expressions.</param>
public sealed record NodeStep(
    string? Id,
    string? Name,
    string? Condition,
    IReadOnlyDictionary<string, string> Env,
    bool ContinueOnError,
    int? TimeoutMinutes,
    string Type,
    JsonElement Inputs,
    IReadOnlyDictionary<string, string> Outputs) : StepDefinition(Id, Name, Condition, Env, ContinueOnError, TimeoutMinutes);
