namespace AutoCanvas.Server.Application.Repositories;

/// <summary>A repository connected in a folder; its credential never comes back.</summary>
/// <param name="Id">The repository's id.</param>
/// <param name="FolderId">The folder it is connected in.</param>
/// <param name="Name">Its name in the folder.</param>
/// <param name="Kind">Which forge it lives on: <c>GenericGit</c> or <c>GitHub</c>.</param>
/// <param name="Url">Its clone address.</param>
/// <param name="DefaultBranch">The branch configured to read automations from; <see langword="null"/> for the repository's own default.</param>
/// <param name="Branch">The branch read at the last sync.</param>
/// <param name="UserName">The user name sent with the credential.</param>
/// <param name="HasCredential">Whether a token or password is stored.</param>
/// <param name="LastSyncedAt">When it was last read.</param>
/// <param name="LastCommit">The commit its automations were last read at.</param>
/// <param name="LastError">Why the last sync failed; <see langword="null"/> when it worked.</param>
/// <param name="PollMinutes">How often the server asks it for new commits, in minutes; 0 when it does not.</param>
/// <param name="NextPollAt">When it is asked next; <see langword="null"/> when polling is off or it is due.</param>
/// <param name="Webhook">Its webhook address, without the token; <see langword="null"/> when it has none.</param>
/// <param name="Problems">Files that are not automations of the folder, and why.</param>
/// <param name="Automations">How many automations come from it.</param>
/// <param name="CreatedAt">When it was connected.</param>
/// <param name="CreatedByName">Who connected it.</param>
public sealed record RepositoryView(
    Guid Id,
    Guid FolderId,
    string Name,
    string Kind,
    Uri Url,
    string? DefaultBranch,
    string? Branch,
    string? UserName,
    bool HasCredential,
    DateTimeOffset? LastSyncedAt,
    string? LastCommit,
    string? LastError,
    int PollMinutes,
    DateTimeOffset? NextPollAt,
    RepositoryWebhookView? Webhook,
    IReadOnlyList<RepositoryProblemView> Problems,
    int Automations,
    DateTimeOffset CreatedAt,
    string CreatedByName);
