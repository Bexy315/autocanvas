namespace AutoCanvas.Server.Api.Automations;

/// <summary>A new key for an automation in its folder.</summary>
/// <param name="Name">1 to 100 characters, no slash; unique in the folder regardless of case.</param>
internal sealed record RenameAutomationRequest(string Name);
