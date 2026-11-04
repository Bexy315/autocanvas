using AutoCanvas.Forge;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// Repositories connected in folders (ADR-0021). The folder's roles decide: Viewer sees them,
/// Operator syncs them, Editor connects, changes and disconnects them. Credentials are encrypted
/// under the master key and never come back.
/// </summary>
public sealed class RepositoryService(
    AutoCanvasDbContext database,
    RepositoryDiscovery discovery,
    SecretCipher cipher,
    IAuditLog audit,
    TimeProvider time)
{
    private readonly FolderAccess access = new(database);

    /// <summary>Connects a Generic Git repository in a folder and reads its automations (Editor).</summary>
    public async Task<UseCaseResult<RepositoryView>> ConnectAsync(Actor actor, Guid folderId, NewRepository request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);
        if (!Uri.TryCreate(request.Address?.Trim(), UriKind.Absolute, out var url)
            || !(string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
            || url.UserInfo.Length > 0 || url.OriginalString.Length > Repository.MaxUrlLength)
        {
            return UseCase.Failure<RepositoryView>(UseCaseError.Invalid("address", "Give the repository's clone address over http or https, such as https://git.example.org/team/shop.git, without a user name or password in it."));
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? NameFrom(url) : request.Name.Trim();
        if ((Names.Check("name", name, Repository.MaxNameLength, "repository") ?? CheckBranch(request.DefaultBranch) ?? CheckUserName(request.UserName) ?? CheckPolling(request.PollMinutes)) is { } invalid)
        {
            return UseCase.Failure<RepositoryView>(invalid);
        }

        var folder = await access.RequireAsync(actor, folderId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<RepositoryView>(denied);
        }

        var normalized = IdentityNames.Normalize(name);
        if (await database.Repositories.AnyAsync(r => r.FolderId == folderId && r.NormalizedName == normalized, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<RepositoryView>(UseCaseError.Conflict($"The folder has a repository named '{name}' already.", "name"));
        }

        var now = time.GetUtcNow();
        var repository = new Repository
        {
            Id = Guid.CreateVersion7(now),
            FolderId = folderId,
            Name = name,
            NormalizedName = normalized,
            Kind = RepositoryKind.GenericGit,
            Url = url,
            DefaultBranch = Empty(request.DefaultBranch),
            UserName = Empty(request.UserName),
            PollMinutes = request.PollMinutes ?? Repository.DefaultPollMinutes,
            CreatedAt = now,
            CreatedByName = await UserNameAsync(actor, cancellationToken).ConfigureAwait(false),
            UpdatedAt = now,
        };
        SetCredential(repository, Empty(request.Secret));

        var (refs, unreadable) = await RefsAsync(repository, cancellationToken).ConfigureAwait(false);
        if (unreadable is not null)
        {
            return UseCase.Failure<RepositoryView>(unreadable);
        }

        repository.NextPollAt = NextPoll(repository, now);
        database.Repositories.Add(repository);
        RepositoryPushes.RecordBaseline(database, repository.Id, refs!);
        audit.Record(Entry(AuditActions.RepositoryConnected, repository, ("address", url.ToString()), ("credential", repository.CredentialCiphertext is not null)));
        await discovery.SyncAsync(actor, repository, cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(await ViewAsync(repository, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The refs of a repository to connect. Only a repository that can be read is connected; git
    /// says why it cannot. Its branches and tags as they are now start nothing: only pushes from
    /// here on do.
    /// </summary>
    private async Task<(IReadOnlyDictionary<string, string>? Refs, UseCaseError? Error)> RefsAsync(Repository repository, CancellationToken cancellationToken)
    {
        try
        {
            var forge = discovery.ForgeOf(repository);
            await forge.ResolveAsync(discovery.AccessOf(repository), repository.DefaultBranch, cancellationToken).ConfigureAwait(false);
            return (await forge.ListRefsAsync(discovery.AccessOf(repository), cancellationToken).ConfigureAwait(false), null);
        }
        catch (ForgeException exception)
        {
            return (null, UseCaseError.Invalid("address", $"The repository cannot be read: {exception.Message}"));
        }
    }

    /// <summary>The repositories connected in a folder, by name (Viewer).</summary>
    public async Task<UseCaseResult<IReadOnlyList<RepositoryView>>> ListAsync(Actor actor, Guid folderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var folder = await access.RequireAsync(actor, folderId, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<IReadOnlyList<RepositoryView>>(denied);
        }

        var repositories = await database.Repositories.AsNoTracking().Where(r => r.FolderId == folderId).OrderBy(r => r.NormalizedName).ToListAsync(cancellationToken).ConfigureAwait(false);
        var views = new List<RepositoryView>();
        foreach (var repository in repositories)
        {
            views.Add(await ViewAsync(repository, cancellationToken).ConfigureAwait(false));
        }

        return UseCase.Success<IReadOnlyList<RepositoryView>>(views);
    }

    /// <summary>One repository (Viewer on its folder).</summary>
    public async Task<UseCaseResult<RepositoryView>> GetAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(actor, id, FolderRole.Viewer, cancellationToken).ConfigureAwait(false);
        return found.Error is { } denied
            ? UseCase.Failure<RepositoryView>(denied)
            : UseCase.Success(await ViewAsync(found.Value!, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Reads the repository again and applies what changed (Operator).</summary>
    public async Task<UseCaseResult<RepositoryView>> SyncAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(actor, id, FolderRole.Operator, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<RepositoryView>(denied);
        }

        await discovery.SyncAsync(actor, found.Value!, cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(await ViewAsync(found.Value!, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Renames the repository, chooses another branch or credential, and reads it again (Editor).</summary>
    public async Task<UseCaseResult<RepositoryView>> UpdateAsync(Actor actor, Guid id, RepositoryChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        if ((CheckBranch(change.DefaultBranch) ?? CheckUserName(change.UserName) ?? CheckPolling(change.PollMinutes)) is { } invalid)
        {
            return UseCase.Failure<RepositoryView>(invalid);
        }

        var found = await FindAsync(actor, id, FolderRole.Editor, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<RepositoryView>(denied);
        }

        var repository = found.Value!;
        var changed = new List<string>();
        if (change.Name is { } newName && !string.Equals(newName.Trim(), repository.Name, StringComparison.Ordinal))
        {
            if (await RenameAsync(repository, newName.Trim(), cancellationToken).ConfigureAwait(false) is { } refused)
            {
                return UseCase.Failure<RepositoryView>(refused);
            }

            changed.Add("name");
        }

        if (change.DefaultBranch is { } branch)
        {
            repository.DefaultBranch = Empty(branch);
            changed.Add("defaultBranch");
        }

        if (change.UserName is { } userName)
        {
            repository.UserName = Empty(userName);
            changed.Add("userName");
        }

        if (change.Secret is { } secret)
        {
            SetCredential(repository, Empty(secret));
            changed.Add("credential");
        }

        if (change.PollMinutes is { } minutes && minutes != repository.PollMinutes)
        {
            repository.PollMinutes = minutes;
            repository.NextPollAt = NextPoll(repository, time.GetUtcNow());
            changed.Add("pollMinutes");
        }

        repository.UpdatedAt = time.GetUtcNow();
        audit.Record(Entry(AuditActions.RepositoryUpdated, repository, ("changed", changed)));
        await discovery.SyncAsync(actor, repository, cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(await ViewAsync(repository, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Disconnects the repository: its automations are deleted, their runs stay (Editor).</summary>
    public async Task<UseCaseResult<Done>> DisconnectAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(actor, id, FolderRole.Editor, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<Done>(denied);
        }

        var repository = found.Value!;
        var automations = await database.Automations.Where(a => a.RepositoryId == id).ToListAsync(cancellationToken).ConfigureAwait(false);
        database.Automations.RemoveRange(automations);
        database.Repositories.Remove(repository);
        audit.Record(Entry(AuditActions.RepositoryDisconnected, repository, ("automations", automations.Count)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        DeleteCache(discovery.CacheOf(id));
        return Done.Result;
    }

    /// <summary>Gives the repository a new name in its folder, or says why it cannot have it.</summary>
    private async Task<UseCaseError?> RenameAsync(Repository repository, string name, CancellationToken cancellationToken)
    {
        if (Names.Check("name", name, Repository.MaxNameLength, "repository") is { } badName)
        {
            return badName;
        }

        var normalized = IdentityNames.Normalize(name);
        if (await database.Repositories.AnyAsync(r => r.FolderId == repository.FolderId && r.NormalizedName == normalized && r.Id != repository.Id, cancellationToken).ConfigureAwait(false))
        {
            return UseCaseError.Conflict($"The folder has a repository named '{name}' already.", "name");
        }

        (repository.Name, repository.NormalizedName) = (name, normalized);
        return null;
    }

    private async Task<UseCaseResult<Repository>> FindAsync(Actor actor, Guid id, FolderRole needed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var repository = await database.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (repository is null)
        {
            return UseCase.Failure<Repository>(NotFound);
        }

        var folder = await access.RequireAsync(actor, repository.FolderId, needed, track: false, cancellationToken).ConfigureAwait(false);
        return folder.Error is { Kind: UseCaseErrorKind.NotFound } ? UseCase.Failure<Repository>(NotFound)
            : folder.Error is { } denied ? UseCase.Failure<Repository>(denied)
            : UseCase.Success(repository);
    }

    private async Task<RepositoryView> ViewAsync(Repository repository, CancellationToken cancellationToken)
    {
        var count = await database.Automations.CountAsync(a => a.RepositoryId == repository.Id, cancellationToken).ConfigureAwait(false);
        return new RepositoryView(
            repository.Id,
            repository.FolderId,
            repository.Name,
            repository.Kind.ToString(),
            repository.Url,
            repository.DefaultBranch,
            repository.ResolvedBranch,
            repository.UserName,
            repository.CredentialCiphertext is not null,
            repository.LastSyncedAt,
            repository.LastCommit,
            repository.LastError,
            repository.PollMinutes,
            repository.NextPollAt,
            RepositoryDiscovery.ProblemsOf(repository),
            count,
            repository.CreatedAt,
            repository.CreatedByName);
    }

    private void SetCredential(Repository repository, string? secret)
    {
        repository.CredentialCiphertext = secret is null ? null : cipher.Encrypt(RepositoryDiscovery.CredentialScope, repository.Id.ToString(), secret);
        repository.CredentialKeyId = secret is null ? null : cipher.Key.Id;
    }

    private async Task<string> UserNameAsync(Actor actor, CancellationToken cancellationToken) =>
        await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? "system";

    private static UseCaseError NotFound => UseCaseError.NotFound("The repository does not exist, or you may not see it.");

    /// <summary>The last part of the address without <c>.git</c>, as the default name.</summary>
    private static string NameFrom(Uri url)
    {
        var last = url.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
        return last.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
    }

    private static UseCaseError? CheckBranch(string? branch) =>
        branch is { Length: > Repository.MaxBranchLength } || branch?.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) == true
            ? UseCaseError.Invalid("defaultBranch", $"A branch name has at most {Repository.MaxBranchLength} characters and no spaces.")
            : null;

    private static UseCaseError? CheckUserName(string? userName) =>
        userName is { Length: > Repository.MaxUserNameLength } || userName?.Any(char.IsControl) == true
            ? UseCaseError.Invalid("userName", $"A user name has at most {Repository.MaxUserNameLength} characters.")
            : null;

    private static UseCaseError? CheckPolling(int? minutes) =>
        minutes is < 0 or > Repository.MaxPollMinutes
            ? UseCaseError.Invalid("pollMinutes", $"Poll every 1 to {Repository.MaxPollMinutes} minutes, or 0 not to poll.")
            : null;

    private static DateTimeOffset? NextPoll(Repository repository, DateTimeOffset now) =>
        repository.PollMinutes == 0 ? null : now.AddMinutes(repository.PollMinutes);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void DeleteCache(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // Git marks its objects read-only, which Windows refuses to delete.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private static AuditEntry Entry(string action, Repository repository, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "repository",
        TargetId = repository.Id.ToString(),
        FolderId = repository.FolderId,
        Details = AuditEntry.DetailsOf([("name", repository.Name), .. details]),
    };
}
