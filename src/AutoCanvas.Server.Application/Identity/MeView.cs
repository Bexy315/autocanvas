namespace AutoCanvas.Server.Application.Identity;

/// <summary>The signed-in user's own account with their groups.</summary>
/// <param name="User">The account.</param>
/// <param name="Groups">The groups the user is in, ordered by name.</param>
public sealed record MeView(UserView User, IReadOnlyList<GroupSummary> Groups);
