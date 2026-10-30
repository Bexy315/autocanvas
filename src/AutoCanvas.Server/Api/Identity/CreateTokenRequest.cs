namespace AutoCanvas.Server.Api.Identity;

/// <summary>An API token to create for the signed-in user.</summary>
/// <param name="Name">What the token is for, e.g. the script or machine using it.</param>
/// <param name="ExpiresAt">When the token stops working; never if omitted.</param>
internal sealed record CreateTokenRequest(string Name, DateTimeOffset? ExpiresAt = null);
