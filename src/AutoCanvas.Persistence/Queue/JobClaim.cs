namespace AutoCanvas.Persistence.Queue;

/// <summary>What a runner can take, and for how long it will hold what it gets.</summary>
/// <param name="RunnerId">The runner asking.</param>
/// <param name="Labels">The runner's labels; a job's <c>runs_on</c> labels must all be among them.</param>
/// <param name="NodeTypes">The node types the runner executes.</param>
/// <param name="RunsContainers">Whether the runner runs jobs in containers (images, services).</param>
/// <param name="Now">The current time; attempts whose retry delay has not passed are left alone.</param>
/// <param name="LeaseUntil">Until when the runner holds the attempt without a heartbeat.</param>
public sealed record JobClaim(
    Guid RunnerId,
    IReadOnlySet<string> Labels,
    IReadOnlySet<string> NodeTypes,
    bool RunsContainers,
    DateTimeOffset Now,
    DateTimeOffset LeaseUntil);
