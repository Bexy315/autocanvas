namespace AutoCanvas.Server.Application.Triggers;

/// <summary>The webhook address of an automation, without its token.</summary>
/// <param name="Enabled">Whether the current version allows webhook calls (<c>triggers.webhook: true</c>); calls fail without it.</param>
/// <param name="Hint">The token's start and end; <see langword="null"/> while there is no address.</param>
/// <param name="RequiresSignature">Whether calls must be signed.</param>
/// <param name="CreatedAt">When the address was created.</param>
/// <param name="CreatedByName">Who created it.</param>
public sealed record WebhookView(bool Enabled, string? Hint, bool RequiresSignature, DateTimeOffset? CreatedAt, string? CreatedByName);
