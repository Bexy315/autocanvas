namespace AutoCanvas.Server.Application.Identity;

/// <summary>A new API token. <see cref="Token"/> is shown this once and cannot be retrieved again.</summary>
/// <param name="Token">The secret to send as <c>Authorization: Bearer …</c>.</param>
/// <param name="Details">The token as it will be listed.</param>
public sealed record CreatedToken(string Token, TokenView Details);
