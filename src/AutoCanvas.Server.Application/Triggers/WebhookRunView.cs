namespace AutoCanvas.Server.Application.Triggers;

/// <summary>The run a webhook call started.</summary>
/// <param name="RunId">The run's id.</param>
/// <param name="Number">Its number in the automation.</param>
public sealed record WebhookRunView(Guid RunId, int Number);
