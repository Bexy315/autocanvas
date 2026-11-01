namespace AutoCanvas.Persistence.Triggers;

/// <summary>
/// An automation's <c>automation_completed</c> trigger, resolved when it was saved: the folder and
/// normalized name of the automation it follows. Kept by name, so that an automation created later
/// under that name is followed too.
/// </summary>
public sealed class CompletionTrigger
{
    /// <summary>The automation that starts.</summary>
    public required Guid AutomationId { get; init; }

    /// <summary>The folder of the automation it follows.</summary>
    public required Guid TargetFolderId { get; set; }

    /// <summary>The normalized name of the automation it follows.</summary>
    public required string TargetName { get; set; }

    /// <summary>Which results of that automation start it.</summary>
    public required CompletionOutcome On { get; set; }

    /// <summary>Navigation to the automation that starts.</summary>
    public Automations.Automation? Automation { get; init; }
}
