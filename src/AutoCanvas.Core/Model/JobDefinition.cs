namespace AutoCanvas.Core.Model;

/// <summary>
/// A job of an automation: a <see cref="StepJob"/> that runs on a runner, or a flow-control job
/// (<see cref="SwitchJob"/>, <see cref="JoinJob"/>) that AutoCanvas resolves itself.
/// </summary>
/// <param name="Id">Job id, the key under <c>jobs</c>.</param>
/// <param name="Name">Display name, or <see langword="null"/> to use the id.</param>
/// <param name="Needs">Jobs that must finish first.</param>
/// <param name="Condition">The job's <c>if</c> expression, or <see langword="null"/>.</param>
public abstract record JobDefinition(
    string Id,
    string? Name,
    IReadOnlyList<JobNeed> Needs,
    string? Condition);
