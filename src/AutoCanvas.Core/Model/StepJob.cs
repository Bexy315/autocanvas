namespace AutoCanvas.Core.Model;

/// <summary>A job that runs steps on a runner.</summary>
/// <param name="Id">Job id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Needs">Jobs that must finish first.</param>
/// <param name="Condition">The job's <c>if</c> expression.</param>
/// <param name="RunsOn">Runner labels a runner must all have.</param>
/// <param name="Image">Container image the steps run in.</param>
/// <param name="Checkout">Which repositories are cloned into the workspace.</param>
/// <param name="Env">Environment variables for all steps, as written.</param>
/// <param name="Services">Containers started next to the job, in file order.</param>
/// <param name="Steps">The steps, in order.</param>
/// <param name="Outputs">What the job publishes to later jobs, as expressions.</param>
/// <param name="Reports">Report files collected after the steps.</param>
/// <param name="TimeoutMinutes">Time limit for this job.</param>
/// <param name="Retry">Whether and how a failed job is run again.</param>
/// <param name="RunsFinally">Runs after all other jobs, whatever their outcome.</param>
/// <param name="RunsOnCancel">For a finally job: also runs when the run is cancelled.</param>
public sealed record StepJob(
    string Id,
    string? Name,
    IReadOnlyList<JobNeed> Needs,
    string? Condition,
    IReadOnlyList<string> RunsOn,
    string? Image,
    CheckoutSetting Checkout,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<ServiceDefinition> Services,
    IReadOnlyList<StepDefinition> Steps,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyList<ReportDefinition> Reports,
    int? TimeoutMinutes,
    RetryPolicy? Retry,
    bool RunsFinally,
    bool RunsOnCancel) : JobDefinition(Id, Name, Needs, Condition);
