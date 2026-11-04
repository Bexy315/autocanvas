using System.Text.Json;
using AutoCanvas.Forge;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// Asks connected repositories for new commits every <see cref="Repository.PollMinutes"/> minutes
/// (ADR-0021) and handles what moved as pushes. Each due repository is taken with a conditional
/// update first, so that of several servers one polls it. The first poll of a repository without
/// recorded refs only records them.
/// </summary>
public sealed class RepositoryPolling
{
    /// <summary>Repositories polled per round; the rest waits for the next.</summary>
    public const int BatchSize = 20;

    /// <summary>Refs handled per poll of a repository; the rest waits for its next poll.</summary>
    public const int MaxPushesPerPoll = 50;

    private static readonly string NoCommit = new('0', 40);

    private readonly AutoCanvasDbContext database;
    private readonly RepositoryDiscovery discovery;
    private readonly RepositoryPushes pushes;
    private readonly RepositoryPullRequests pullRequests;
    private readonly TimeProvider time;

    /// <summary>Creates the polling for one unit of work.</summary>
    public RepositoryPolling(AutoCanvasDbContext database, RepositoryDiscovery discovery, GitHubAccess github, IJobSignal signal, ConfigurationService configuration, IAuditLog audit, TimeProvider time)
    {
        this.database = database;
        this.discovery = discovery;
        this.time = time;
        pushes = new RepositoryPushes(database, discovery, signal, configuration, audit, time);
        pullRequests = new RepositoryPullRequests(database, discovery, github, signal, configuration, audit, time);
    }

    /// <summary>Handles the pushes and pull requests webhooks stored, oldest first; each is taken out by one server.</summary>
    public async Task<TriggerRound> HandleReceivedAsync(CancellationToken cancellationToken)
    {
        var round = await HandlePushesAsync(cancellationToken).ConfigureAwait(false);
        var pulls = await database.RepositoryPullRequests.AsNoTracking()
            .OrderBy(p => p.ReceivedAt).ThenBy(p => p.Id)
            .Take(MaxPushesPerPoll)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var pull in pulls)
        {
            if (await database.RepositoryPullRequests.Where(p => p.Id == pull.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 1
                && await database.Repositories.FirstOrDefaultAsync(r => r.Id == pull.RepositoryId, cancellationToken).ConfigureAwait(false) is { } repository)
            {
                await pullRequests.HandleAsync(repository, pull, round, cancellationToken).ConfigureAwait(false);
            }

            database.ChangeTracker.Clear();
        }

        return round;
    }

    private async Task<TriggerRound> HandlePushesAsync(CancellationToken cancellationToken)
    {
        var received = await database.RepositoryPushes.AsNoTracking()
            .OrderBy(p => p.ReceivedAt).ThenBy(p => p.Id)
            .Take(MaxPushesPerPoll)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var round = new TriggerRound();
        foreach (var stored in received)
        {
            if (await database.RepositoryPushes.Where(p => p.Id == stored.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                continue;
            }

            if (await database.Repositories.FirstOrDefaultAsync(r => r.Id == stored.RepositoryId, cancellationToken).ConfigureAwait(false) is { } repository)
            {
                var push = new PushEvent(stored.Ref, stored.Before, stored.After)
                {
                    ChangedFiles = stored.ChangedFiles is { } files ? JsonSerializer.Deserialize<List<string>>(files) : null,
                    Pusher = stored.Pusher,
                    Message = stored.Message,
                };
                await pushes.HandleAsync(repository, push, round, cancellationToken).ConfigureAwait(false);
            }

            database.ChangeTracker.Clear();
        }

        return round;
    }

    /// <summary>Polls every repository that is due.</summary>
    public async Task<TriggerRound> PollDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await database.Repositories.AsNoTracking()
            .Where(r => r.PollMinutes > 0 && (r.NextPollAt == null || r.NextPollAt <= now))
            .OrderBy(r => r.NextPollAt)
            .Select(r => new { r.Id, r.NextPollAt, r.PollMinutes })
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var round = new TriggerRound();
        foreach (var repository in due)
        {
            var next = now.AddMinutes(repository.PollMinutes);
            var taken = await database.Repositories
                .Where(r => r.Id == repository.Id && r.NextPollAt == repository.NextPollAt)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.NextPollAt, next), cancellationToken).ConfigureAwait(false);
            if (taken == 1)
            {
                await PollAsync(repository.Id, round, cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
            }
        }

        return round;
    }

    private async Task PollAsync(Guid id, TriggerRound round, CancellationToken cancellationToken)
    {
        var repository = await database.Repositories.FirstAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> remote;
        try
        {
            remote = await discovery.ForgeOf(repository).ListRefsAsync(await discovery.AccessAsync(repository, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            repository.LastError = exception.Message.Length > Repository.MaxErrorLength ? exception.Message[..Repository.MaxErrorLength] : exception.Message;
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            round.Skip($"Polling the repository '{repository.Name}' failed: {exception.Message}");
            return;
        }

        var known = await database.RepositoryRefs.AsNoTracking().Where(r => r.RepositoryId == id).ToDictionaryAsync(r => r.Ref, r => r.Commit, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        if (known.Count == 0)
        {
            RepositoryPushes.RecordBaseline(database, id, remote);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var moved = Moved(known, remote, RepositoryPushes.BranchRefOf(repository));
        if (repository.LastError is not null && !moved.Any(p => string.Equals(p.Ref, RepositoryPushes.BranchRefOf(repository), StringComparison.Ordinal)))
        {
            // The repository answers again: read it, so that the error goes once it is fixed.
            await discovery.SyncAsync(RepositoryPushes.System, repository, cancellationToken).ConfigureAwait(false);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var push in moved)
        {
            await pushes.HandleAsync(repository, push, round, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>What moved since the last poll, the repository's own branch first (its push syncs the automations).</summary>
    private static List<PushEvent> Moved(Dictionary<string, string> known, IReadOnlyDictionary<string, string> remote, string? branchRef)
    {
        var moved = new List<PushEvent>();
        foreach (var (gitRef, commit) in remote)
        {
            var before = known.GetValueOrDefault(gitRef);
            if (!string.Equals(before, commit, StringComparison.Ordinal))
            {
                moved.Add(new PushEvent(gitRef, before ?? NoCommit, commit));
            }
        }

        moved.AddRange(known.Where(k => !remote.ContainsKey(k.Key)).Select(k => new PushEvent(k.Key, k.Value, NoCommit)));
        return [.. moved.OrderBy(p => string.Equals(p.Ref, branchRef, StringComparison.Ordinal) ? 0 : 1).Take(MaxPushesPerPoll)];
    }
}
