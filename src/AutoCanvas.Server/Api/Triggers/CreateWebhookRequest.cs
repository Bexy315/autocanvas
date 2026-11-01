namespace AutoCanvas.Server.Api.Triggers;

/// <summary>A new webhook address.</summary>
/// <param name="RequireSignature">Whether calls must be signed with a secret the server makes; false by default.</param>
internal sealed record CreateWebhookRequest(bool RequireSignature = false);
