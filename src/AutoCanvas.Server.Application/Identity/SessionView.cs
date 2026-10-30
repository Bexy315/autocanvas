namespace AutoCanvas.Server.Application.Identity;

/// <summary>One of the user's sessions, so they can recognize and end the ones they do not know.</summary>
/// <param name="Id">The session's id.</param>
/// <param name="CreatedAt">When the user signed in.</param>
/// <param name="LastSeenAt">The last request with this session (to the minute).</param>
/// <param name="ExpiresAt">When the session ends at the latest.</param>
/// <param name="ClientAddress">Network address of the sign-in.</param>
/// <param name="UserAgent">Browser of the sign-in.</param>
/// <param name="IsCurrent">Whether this is the session making the request.</param>
public sealed record SessionView(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    string? ClientAddress,
    string? UserAgent,
    bool IsCurrent);
