namespace AutoCanvas.Core.Model;

/// <summary>A step that runs a shell script.</summary>
/// <param name="Id">Step id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Condition">The step's <c>if</c> expression.</param>
/// <param name="Env">Environment variables for this step.</param>
/// <param name="ContinueOnError">A failure of this step does not fail the job.</param>
/// <param name="TimeoutMinutes">Time limit for this step.</param>
/// <param name="Run">The script, as written (may contain expressions).</param>
/// <param name="Shell">Shell to use, or <see langword="null"/> for the runner's default.</param>
/// <param name="WorkingDirectory">Directory relative to the workspace.</param>
public sealed record ScriptStep(
    string? Id,
    string? Name,
    string? Condition,
    IReadOnlyDictionary<string, string> Env,
    bool ContinueOnError,
    int? TimeoutMinutes,
    string Run,
    ScriptShell? Shell,
    string? WorkingDirectory) : StepDefinition(Id, Name, Condition, Env, ContinueOnError, TimeoutMinutes);
