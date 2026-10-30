namespace AutoCanvas.Server.Application.Identity;

/// <summary>A user as shown in member lists.</summary>
/// <param name="Id">The user's id.</param>
/// <param name="UserName">The name used to sign in.</param>
/// <param name="DisplayName">The name shown to other people.</param>
public sealed record UserSummary(Guid Id, string UserName, string DisplayName);
