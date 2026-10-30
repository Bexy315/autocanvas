namespace AutoCanvas.Core.Model;

/// <summary>A flow-control job that waits for its needs and passes through.</summary>
/// <param name="Id">Job id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Needs">Jobs that must finish first.</param>
/// <param name="Condition">The job's <c>if</c> expression.</param>
public sealed record JoinJob(
    string Id,
    string? Name,
    IReadOnlyList<JobNeed> Needs,
    string? Condition) : JobDefinition(Id, Name, Needs, Condition);
