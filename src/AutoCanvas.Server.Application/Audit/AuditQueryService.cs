using System.Buffers.Text;
using System.Globalization;
using System.Text;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Audit;

/// <summary>
/// Reading the audit log: admins read all of it, folder owners the entries of the folders they own.
/// Newest first, one page at a time.
/// </summary>
public sealed class AuditQueryService(AutoCanvasDbContext database)
{
    private readonly FolderAccess folders = new(database);

    /// <summary>All entries (admins only).</summary>
    public async Task<UseCaseResult<Page<AuditEventView>>> ListAsync(Actor actor, AuditFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.IsAdmin
            ? await QueryAsync(database.AuditEvents.AsNoTracking(), filter, page, cancellationToken).ConfigureAwait(false)
            : UseCase.Failure<Page<AuditEventView>>(UseCaseError.Forbidden("Only admins read the whole audit log; folder owners read the log of their folders."));
    }

    /// <summary>
    /// Entries of a folder and of the folders below it that the actor owns too (Owner on the folder).
    /// A restricted subfolder the actor does not own stays out.
    /// </summary>
    public async Task<UseCaseResult<Page<AuditEventView>>> ListForFolderAsync(Actor actor, Guid folderId, AuditFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var folder = await folders.RequireAsync(actor, folderId, FolderRole.Owner, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<Page<AuditEventView>>(denied);
        }

        var owned = await OwnedSubtreeAsync(actor, folder.Value!, cancellationToken).ConfigureAwait(false);
        var events = database.AuditEvents.AsNoTracking().Where(e => e.FolderId != null && owned.Contains(e.FolderId.Value));
        return await QueryAsync(events, filter, page, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The folder and every folder below it on which the actor is Owner.</summary>
    private async Task<List<Guid>> OwnedSubtreeAsync(Actor actor, AccessibleFolder top, CancellationToken cancellationToken)
    {
        var path = top.Folder.Path;
        var below = await database.Folders.AsNoTracking()
            .Where(f => f.Path.StartsWith(path) && f.Id != top.Folder.Id)
            .Select(f => new { f.Id, f.Path, Entry = new FolderChainEntry(f.Id, f.Name, f.IsRestricted) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var entries = top.Chain.ToDictionary(c => c.Id);
        foreach (var folder in below)
        {
            entries[folder.Id] = folder.Entry;
        }

        var grants = actor.IsAdmin ? [] : await folders.GrantsAsync(actor, [.. entries.Keys], cancellationToken).ConfigureAwait(false);
        var owned = new List<Guid> { top.Folder.Id };
        foreach (var folder in below)
        {
            var chain = FolderPaths.Ids(folder.Path).Select(id => entries[id]).ToList();
            if (FolderPermissions.EffectiveRole(chain, grants, actor.IsAdmin) >= FolderRole.Owner)
            {
                owned.Add(folder.Id);
            }
        }

        return owned;
    }

    private static async Task<UseCaseResult<Page<AuditEventView>>> QueryAsync(IQueryable<AuditEvent> events, AuditFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        var after = AuditCursor.Decode(page.Cursor);
        if ((limit.Error ?? after.Error) is { } invalid)
        {
            return UseCase.Failure<Page<AuditEventView>>(invalid);
        }

        events = Filter(events, filter);

        // Keyset by time, newest first. Entries of the same instant are told apart by how many of
        // them the previous pages returned, so no id comparison is needed (not every provider can).
        var skip = 0;
        if (after.Value is { } cursor)
        {
            events = events.Where(e => e.OccurredAt <= cursor.At);
            skip = cursor.SameInstant;
        }

        var rows = await events.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(limit.Value + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Take(limit.Value).ToList();
        string? next = null;
        if (rows.Count > limit.Value)
        {
            var last = items[^1].OccurredAt;
            var sameInstant = items.Count(e => e.OccurredAt == last) + (after.Value is { } previous && previous.At == last ? previous.SameInstant : 0);
            next = AuditCursor.Encode(last, sameInstant);
        }

        return UseCase.Success(new Page<AuditEventView>([.. items.Select(AuditEventView.From)], next));
    }

    private static IQueryable<AuditEvent> Filter(IQueryable<AuditEvent> events, AuditFilter filter)
    {
        if (filter.From is { } from)
        {
            events = events.Where(e => e.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            events = events.Where(e => e.OccurredAt < to);
        }

        if (filter.Action is { Length: > 0 } action)
        {
            if (action.EndsWith('*'))
            {
                var prefix = action[..^1];
                events = events.Where(e => e.Action.StartsWith(prefix));
            }
            else
            {
                events = events.Where(e => e.Action == action);
            }
        }

        if (filter.ActorId is { } actorId)
        {
            events = events.Where(e => e.ActorId == actorId);
        }

        if (filter.TargetType is { Length: > 0 } targetType)
        {
            events = events.Where(e => e.TargetType == targetType);
        }

        if (filter.TargetId is { Length: > 0 } targetId)
        {
            events = events.Where(e => e.TargetId == targetId);
        }

        return events;
    }

    /// <summary>Where the previous page ended: its last instant and how many entries of that instant it covered.</summary>
    private sealed record AuditCursor(DateTimeOffset At, int SameInstant)
    {
        public static string Encode(DateTimeOffset at, int sameInstant) =>
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{at.UtcTicks}:{sameInstant}")));

        public static UseCaseResult<AuditCursor?> Decode(string? cursor)
        {
            if (cursor is null)
            {
                return UseCase.Success<AuditCursor?>(null);
            }

            try
            {
                var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split(':');
                if (parts.Length == 2
                    && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                    && ticks <= DateTimeOffset.MaxValue.UtcTicks
                    && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var same))
                {
                    return UseCase.Success<AuditCursor?>(new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), same));
                }
            }
            catch (FormatException)
            {
                // Falls through to the error below.
            }

            return UseCase.Failure<AuditCursor?>(UseCaseError.Invalid("cursor", "cursor is not a value from a previous page."));
        }
    }
}
