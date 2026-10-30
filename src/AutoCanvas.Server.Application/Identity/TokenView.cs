namespace AutoCanvas.Server.Application.Identity;

/// <summary>An API token as listed: everything except the secret, which is shown only once.</summary>
/// <param name="Id">The token's id.</param>
/// <param name="Name">What the token is used for.</param>
/// <param name="Hint">The start and last characters, to recognize the token.</param>
/// <param name="CreatedAt">When the token was created.</param>
/// <param name="ExpiresAt">When the token stops working; never if empty.</param>
/// <param name="LastUsedAt">The last request made with it (to the minute).</param>
/// <param name="IsExpired">Whether the token no longer works because it expired.</param>
public sealed record TokenView(
    Guid Id,
    string Name,
    string Hint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsExpired);
