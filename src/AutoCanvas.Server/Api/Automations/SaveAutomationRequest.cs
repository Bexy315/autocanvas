namespace AutoCanvas.Server.Api.Automations;

/// <summary>A new version of an automation.</summary>
/// <param name="Yaml">The whole automation file; it must be valid and differ from the current version.</param>
/// <param name="Message">What changed, in your words.</param>
internal sealed record SaveAutomationRequest(string Yaml, string? Message = null);
