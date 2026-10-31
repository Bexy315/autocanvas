namespace AutoCanvas.Server.Api.Automations;

/// <summary>An automation to create in a folder.</summary>
/// <param name="Name">The key in the folder, like a file name: 1 to 100 characters, no slash; unique in the folder regardless of case.</param>
/// <param name="Yaml">The automation file; it must be valid, else the problem lists every diagnostic.</param>
/// <param name="Message">What this first version is, in your words.</param>
internal sealed record CreateAutomationRequest(string Name, string Yaml, string? Message = null);
