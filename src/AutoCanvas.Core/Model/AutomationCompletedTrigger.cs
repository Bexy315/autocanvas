namespace AutoCanvas.Core.Model;

/// <summary>Starts runs when another automation finishes.</summary>
/// <param name="Automation">Name or path of the other automation.</param>
/// <param name="On">Which outcome of the other automation counts.</param>
public sealed record AutomationCompletedTrigger(string Automation, OutcomeFilter On);
