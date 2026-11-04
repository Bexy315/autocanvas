namespace AutoCanvas.Server.Application.Forges;

/// <summary>What GitHub hands the browser back after creating the app.</summary>
/// <param name="Code">The code to turn into the app and its secrets, valid for an hour.</param>
/// <param name="State">The state of the manifest it was created from.</param>
public sealed record GitHubAppCompletion(string Code, string State);
