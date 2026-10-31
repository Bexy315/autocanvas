namespace AutoCanvas.Server.Api.Runners;

/// <summary>A runner registration token to create.</summary>
/// <param name="Description">What the token is for, such as the machine the runner will run on.</param>
/// <param name="ExpiresAt">When the token stops working if unused; at most 30 days away. Default: in 24 hours.</param>
internal sealed record CreateRegistrationTokenRequest(string? Description = null, DateTimeOffset? ExpiresAt = null);
