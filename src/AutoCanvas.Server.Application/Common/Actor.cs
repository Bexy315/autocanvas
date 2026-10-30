namespace AutoCanvas.Server.Application.Common;

/// <summary>Who calls a use case that checks folder permissions.</summary>
/// <param name="UserId">The signed-in user.</param>
/// <param name="IsAdmin">Instance admins have every role on every folder (ADR-0012).</param>
public sealed record Actor(Guid UserId, bool IsAdmin);
