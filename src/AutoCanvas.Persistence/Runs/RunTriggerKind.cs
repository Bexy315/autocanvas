namespace AutoCanvas.Persistence.Runs;

/// <summary>What started a run; readable as <c>trigger.event</c>.</summary>
public enum RunTriggerKind
{
    /// <summary>Someone started it.</summary>
    Manual,

    /// <summary>A schedule.</summary>
    Schedule,

    /// <summary>A webhook call.</summary>
    Webhook,

    /// <summary>Another automation finished.</summary>
    AutomationCompleted,

    /// <summary>A push to the automation's repository.</summary>
    Push,

    /// <summary>A pull request in the automation's repository.</summary>
    PullRequest,
}
