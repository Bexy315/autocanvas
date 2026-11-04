namespace AutoCanvas.Core.Model;

/// <summary>What starts runs of an automation.</summary>
/// <param name="Manual">Runs can be started by hand.</param>
/// <param name="Push">Runs start on pushes to the automation's repository.</param>
/// <param name="Schedules">Time-based triggers.</param>
/// <param name="Webhook">Runs can be started by a signed HTTP call.</param>
/// <param name="AutomationCompleted">Runs start when another automation finishes.</param>
/// <param name="PullRequest">Runs start on pull requests in the automation's repository.</param>
public sealed record Triggers(
    bool Manual,
    PushTrigger? Push,
    IReadOnlyList<ScheduleTrigger> Schedules,
    bool Webhook,
    AutomationCompletedTrigger? AutomationCompleted,
    PullRequestTrigger? PullRequest)
{
    /// <summary>No triggers: the automation only runs when started by hand or by another automation.</summary>
    public static Triggers None { get; } = new(Manual: false, Push: null, Schedules: [], Webhook: false, AutomationCompleted: null, PullRequest: null);
}
