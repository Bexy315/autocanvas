namespace AutoCanvas.Server.Api.Automations;

/// <summary>Where to move an automation.</summary>
/// <param name="FolderId">The folder to move it to; needs Editor there as well as on the current folder.</param>
internal sealed record MoveAutomationRequest(Guid FolderId);
