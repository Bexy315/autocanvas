namespace AutoCanvas.Server.Api.Folders;

/// <summary>Whether grants from above apply to a folder.</summary>
/// <param name="IsRestricted">True to stop inheritance at this folder; false to lift that.</param>
internal sealed record SetRestrictionRequest(bool IsRestricted);
