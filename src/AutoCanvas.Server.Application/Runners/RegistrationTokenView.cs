namespace AutoCanvas.Server.Application.Runners;

/// <summary>A runner registration token as listed: everything except the secret.</summary>
/// <param name="Id">The token's id.</param>
/// <param name="Description">What the token is for.</param>
/// <param name="Hint">The start and last characters, to recognize the token.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="CreatedBy">The administrator who created it.</param>
/// <param name="ExpiresAt">When it stops working if unused.</param>
/// <param name="UsedAt">When a runner registered with it; it cannot be used again.</param>
/// <param name="RunnerId">The runner that registered with it, while that runner exists.</param>
/// <param name="IsUsable">Whether a runner can still register with it: not used and not expired.</param>
public sealed record RegistrationTokenView(
    Guid Id,
    string? Description,
    string Hint,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    Guid? RunnerId,
    bool IsUsable);
