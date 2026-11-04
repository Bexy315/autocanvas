namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// What the browser sends to GitHub to create the app: a form posted to <see cref="Action"/> with
/// the field <c>manifest</c>.
/// </summary>
/// <param name="Action">The address to post the form to, with the state in its query.</param>
/// <param name="Manifest">The app's manifest, as JSON.</param>
/// <param name="State">The state GitHub hands back with the code; valid for an hour.</param>
public sealed record GitHubManifestView(Uri Action, string Manifest, string State);
