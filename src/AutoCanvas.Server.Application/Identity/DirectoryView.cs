namespace AutoCanvas.Server.Application.Identity;

/// <summary>People and groups found by a search, names whose start matches first.</summary>
/// <param name="Users">Users that are not disabled, without their e-mail addresses.</param>
/// <param name="Groups">Groups.</param>
public sealed record DirectoryView(IReadOnlyList<UserSummary> Users, IReadOnlyList<GroupSummary> Groups);
