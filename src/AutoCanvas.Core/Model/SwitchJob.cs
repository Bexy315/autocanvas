namespace AutoCanvas.Core.Model;

/// <summary>A flow-control job that activates one of its cases by the value of an expression.</summary>
/// <param name="Id">Job id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Needs">Jobs that must finish first.</param>
/// <param name="Condition">The job's <c>if</c> expression.</param>
/// <param name="Expression">Expression whose value selects the case.</param>
/// <param name="Cases">The cases, in order; the first match wins.</param>
public sealed record SwitchJob(
    string Id,
    string? Name,
    IReadOnlyList<JobNeed> Needs,
    string? Condition,
    string Expression,
    IReadOnlyList<SwitchCase> Cases) : JobDefinition(Id, Name, Needs, Condition);
