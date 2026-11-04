using System.Text.Json;
using AutoCanvas.Core.Model;
using AutoCanvas.Forge;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// Handles a push to a connected repository (ADR-0021), whether a poll or a webhook saw it. Each
/// push is handled once: whoever moves the ref's row to the new commit first. A push to the
/// repository's branch syncs its automations first; then every repository automation whose file
/// exists at the pushed ref and whose <c>triggers.push</c> there matches starts a run with that
/// file, at that commit.
/// </summary>
internal sealed class RepositoryPushes
{
    /// <summary>Who syncs after a push: the server, which sees every folder.</summary>
    internal static readonly Actor System = new(Guid.Empty, IsAdmin: true);

    private readonly AutoCanvasDbContext database;
    private readonly RepositoryDiscovery discovery;
    private readonly RunStarter starter;

    public RepositoryPushes(AutoCanvasDbContext database, RepositoryDiscovery discovery, IJobSignal signal, ConfigurationService configuration, IAuditLog audit, TimeProvider time)
    {
        this.database = database;
        this.discovery = discovery;
        starter = new RunStarter(database, signal, audit, new RunEngine(database, configuration, time), time);
    }

    /// <summary>
    /// Handles a push. <paramref name="repository"/> is tracked; changes to it are saved before
    /// any run starts.
    /// </summary>
    public async Task HandleAsync(Repository repository, PushEvent push, TriggerRound round, CancellationToken cancellationToken)
    {
        if (!GenericGitForge.IsFullRef(push.Ref))
        {
            return;
        }

        if (PushEvent.IsNone(push.After))
        {
            await database.RepositoryRefs.Where(r => r.RepositoryId == repository.Id && r.Ref == push.Ref).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (await RecordAsync(repository.Id, push.Ref, push.After, cancellationToken).ConfigureAwait(false) is not { } previous)
        {
            return;
        }

        if (string.Equals(push.Ref, BranchRefOf(repository), StringComparison.Ordinal))
        {
            await discovery.SyncAsync(System, repository, cancellationToken).ConfigureAwait(false);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var forge = discovery.ForgeOf(repository);
        if (await ReadAsync(repository, forge, push, round, cancellationToken).ConfigureAwait(false) is not var (snapshot, access))
        {
            return;
        }

        if (!string.Equals(snapshot.Commit, push.After, StringComparison.Ordinal))
        {
            // The ref moved on again; the push that moved it starts the runs, with its files. When
            // that push was handled already (a webhook arriving late), the row goes back to it, so
            // that it is not handled again.
            if (string.Equals(previous, snapshot.Commit, StringComparison.Ordinal))
            {
                await MoveAsync(repository.Id, push.Ref, push.After, previous, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var changes = new ChangedFiles(push, forge, access);
        var automations = await database.Automations.AsNoTracking().Where(a => a.RepositoryId == repository.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var file in snapshot.Files)
        {
            if (automations.FirstOrDefault(a => string.Equals(a.SourcePath, file.Path, StringComparison.Ordinal)) is not { } automation)
            {
                // Only on this branch so far: the folder has no automation for it.
                continue;
            }

            await StartAsync(automation, file, push, snapshot, changes, round, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The automation files at the pushed ref, or <see langword="null"/> when they cannot be read.</summary>
    private async Task<(RepositorySnapshot Snapshot, RepositoryAccess Access)?> ReadAsync(Repository repository, IForge forge, PushEvent push, TriggerRound round, CancellationToken cancellationToken)
    {
        try
        {
            var access = await discovery.AccessAsync(repository, cancellationToken).ConfigureAwait(false);
            return (await forge.ReadAutomationsAsync(access, push.Ref, cancellationToken).ConfigureAwait(false), access);
        }
        catch (ForgeException exception)
        {
            round.Skip($"The push to {push.Ref} of the repository '{repository.Name}' started nothing: {exception.Message}");
            return null;
        }
    }

    /// <summary>The baseline of a repository: its refs as they are, recorded without runs (saved by the caller).</summary>
    public static void RecordBaseline(AutoCanvasDbContext database, Guid repositoryId, IReadOnlyDictionary<string, string> refs)
    {
        foreach (var (gitRef, commit) in refs)
        {
            database.RepositoryRefs.Add(new RepositoryRef { RepositoryId = repositoryId, Ref = gitRef, Commit = commit });
        }
    }

    /// <summary>The ref of the branch the repository's automations are read from.</summary>
    internal static string? BranchRefOf(Repository repository) =>
        (repository.ResolvedBranch ?? repository.DefaultBranch) is { } branch ? PushTrigger.BranchPrefix + branch : null;

    private async Task StartAsync(Automation automation, RepositoryFile file, PushEvent push, RepositorySnapshot snapshot, ChangedFiles changes, TriggerRound round, CancellationToken cancellationToken)
    {
        var loaded = AutomationService.Load(file.Content);
        if (loaded.Value is not { } definition)
        {
            round.Skip($"'{automation.Name}' did not start (push to {push.Ref}): its file there is not valid.");
            return;
        }

        if (definition.Triggers.Push is not { } trigger
            || !trigger.Matches(push.Ref, trigger.Paths.Count == 0 ? null : await changes.GetAsync(cancellationToken).ConfigureAwait(false)))
        {
            return;
        }

        var pusher = Cut(push.Pusher ?? (snapshot.Author.Length > 0 ? snapshot.Author : null), Run.MaxTriggeredByNameLength);
        var start = new RunStart(RunTriggerKind.Push, null, pusher ?? "push", Data(push, snapshot, pusher))
        {
            Commit = snapshot.Commit,
            Ref = push.Ref,
        };
        var started = await starter.StartAsync(automation, file.Content, definition, start, cancellationToken).ConfigureAwait(false);
        if (started.Error is { } refused)
        {
            round.Skip($"'{automation.Name}' did not start (push to {push.Ref}): {refused.Message}");
            return;
        }

        round.Start($"'{automation.Name}' #{started.Value!.Number}");
    }

    /// <summary>
    /// Moves the ref's row to the commit, or adds it, and says what it was before (an empty
    /// commit for a new row); <see langword="null"/> when it is there already, so that the push was
    /// handled (by a webhook, a poll or another server), or when another handler moved it meanwhile.
    /// </summary>
    private async Task<string?> RecordAsync(Guid repositoryId, string gitRef, string commit, CancellationToken cancellationToken)
    {
        var previous = await database.RepositoryRefs.AsNoTracking()
            .Where(r => r.RepositoryId == repositoryId && r.Ref == gitRef)
            .Select(r => r.Commit)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(previous, commit, StringComparison.Ordinal))
        {
            return null;
        }

        if (previous is not null)
        {
            return await MoveAsync(repositoryId, gitRef, previous, commit, cancellationToken).ConfigureAwait(false) ? previous : null;
        }

        var row = new RepositoryRef { RepositoryId = repositoryId, Ref = gitRef, Commit = commit };
        database.RepositoryRefs.Add(row);
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return string.Empty;
        }
        catch (DbUpdateException)
        {
            // Another handler added it in the meantime.
            database.Entry(row).State = EntityState.Detached;
            return null;
        }
    }

    /// <summary>Moves the ref's row from one commit to another, if no one moved it meanwhile.</summary>
    private async Task<bool> MoveAsync(Guid repositoryId, string gitRef, string from, string to, CancellationToken cancellationToken) =>
        await database.RepositoryRefs
            .Where(r => r.RepositoryId == repositoryId && r.Ref == gitRef && r.Commit == from)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Commit, to), cancellationToken).ConfigureAwait(false) == 1;

    /// <summary>What the run reads as <c>trigger.*</c>.</summary>
    private static string Data(PushEvent push, RepositorySnapshot snapshot, string? pusher)
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ref"] = push.Ref,
            ["sha"] = snapshot.Commit,
            ["short_sha"] = snapshot.Commit[..Math.Min(7, snapshot.Commit.Length)],
            ["before"] = push.Before,
            ["pusher"] = pusher,
            ["message"] = push.Message ?? snapshot.Subject,
        };
        if (push.Ref.StartsWith(PushTrigger.BranchPrefix, StringComparison.Ordinal))
        {
            data["branch"] = push.Ref[PushTrigger.BranchPrefix.Length..];
        }
        else
        {
            data["tag"] = push.Ref[PushTrigger.TagPrefix.Length..];
        }

        return JsonSerializer.Serialize(data);
    }

    private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

    /// <summary>The files a push changed, asked from the forge at most once and only when a path filter needs them.</summary>
    private sealed class ChangedFiles(PushEvent push, IForge forge, RepositoryAccess access)
    {
        private bool asked;
        private IReadOnlyList<string>? files = push.ChangedFiles;

        public async Task<IReadOnlyList<string>?> GetAsync(CancellationToken cancellationToken)
        {
            if (files is not null || asked)
            {
                return files;
            }

            asked = true;
            try
            {
                files = await forge.ChangedFilesAsync(access, push.Ref, push.Before, push.After, cancellationToken).ConfigureAwait(false);
            }
            catch (ForgeException)
            {
                // Not known; any change counts then.
                files = null;
            }

            return files;
        }
    }
}
