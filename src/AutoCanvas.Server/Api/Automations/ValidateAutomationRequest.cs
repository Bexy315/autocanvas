namespace AutoCanvas.Server.Api.Automations;

/// <summary>A file to check without saving it.</summary>
/// <param name="Yaml">The whole automation file.</param>
/// <param name="FolderId">The folder it would be saved in; its triggers are checked from there.</param>
/// <param name="AutomationId">The automation being edited, if any; it must be in that folder.</param>
internal sealed record ValidateAutomationRequest(string Yaml, Guid FolderId, Guid? AutomationId = null);
