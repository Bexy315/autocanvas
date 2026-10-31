namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// A job attempt as it is offered to a runner: fully resolved, without expressions and without
/// secrets — those travel only with the steps that use them (ADR-0008).
/// </summary>
/// <param name="AttemptId">The attempt; the protocol's <c>job_id</c>.</param>
/// <param name="RunId">The run.</param>
/// <param name="Attempt">Attempt number, starting at 1.</param>
/// <param name="AutomationName">Name of the automation.</param>
/// <param name="JobKey">The job's id in the automation.</param>
/// <param name="DisplayName">The job's display name.</param>
/// <param name="Timeout">Time limit of the whole job.</param>
/// <param name="Image">Container image, or <see langword="null"/> for the runner's default.</param>
/// <param name="Checkouts">Repositories to clone, in order.</param>
/// <param name="Services">Containers to start next to the job.</param>
/// <param name="Env">Environment of all steps.</param>
/// <param name="Reports">Report files to collect at the end.</param>
public sealed record OfferedJob(
    Guid AttemptId,
    Guid RunId,
    int Attempt,
    string AutomationName,
    string JobKey,
    string DisplayName,
    TimeSpan Timeout,
    string? Image,
    IReadOnlyList<OfferedCheckout> Checkouts,
    IReadOnlyList<OfferedService> Services,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<OfferedReport> Reports);
