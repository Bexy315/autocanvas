namespace AutoCanvas.Forge.GitHub;

/// <summary>Where a GitHub App is installed.</summary>
/// <param name="Id">The installation's id.</param>
/// <param name="Account">The account or organization it is installed on.</param>
/// <param name="AccountType"><c>User</c> or <c>Organization</c>.</param>
/// <param name="AllRepositories">Whether it covers all of the account's repositories, or chosen ones.</param>
/// <param name="Suspended">Whether it is suspended.</param>
public sealed record GitHubInstallation(long Id, string Account, string AccountType, bool AllRepositories, bool Suspended);
