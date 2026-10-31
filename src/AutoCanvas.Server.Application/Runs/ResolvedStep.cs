namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// A step as it is sent to the runner (ADR-0008): fully resolved, with the secrets it uses in its
/// environment and the values the runner must mask.
/// </summary>
/// <param name="Index">Position of the step in the job, from 0.</param>
/// <param name="StepId">The step's id, if it has one.</param>
/// <param name="Name">Display name.</param>
/// <param name="Timeout">Time limit of the step; <see langword="null"/> when only the job's applies.</param>
/// <param name="Env">Environment on top of the job's, secrets included.</param>
/// <param name="MaskValues">Every secret value the step can see.</param>
/// <param name="Script">The script, for a script step.</param>
/// <param name="Node">The node type and inputs, for a node step.</param>
public sealed record ResolvedStep(
    int Index,
    string? StepId,
    string Name,
    TimeSpan? Timeout,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<string> MaskValues,
    ResolvedScript? Script,
    ResolvedNode? Node);
