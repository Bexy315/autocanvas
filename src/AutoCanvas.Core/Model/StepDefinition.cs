namespace AutoCanvas.Core.Model;

/// <summary>A step of a <see cref="StepJob"/>: a <see cref="ScriptStep"/> or a <see cref="NodeStep"/>.</summary>
/// <param name="Id">Step id for reading its outputs, or <see langword="null"/>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Condition">The step's <c>if</c> expression.</param>
/// <param name="Env">Environment variables for this step, as written.</param>
/// <param name="ContinueOnError">A failure of this step does not fail the job.</param>
/// <param name="TimeoutMinutes">Time limit for this step.</param>
public abstract record StepDefinition(
    string? Id,
    string? Name,
    string? Condition,
    IReadOnlyDictionary<string, string> Env,
    bool ContinueOnError,
    int? TimeoutMinutes);
