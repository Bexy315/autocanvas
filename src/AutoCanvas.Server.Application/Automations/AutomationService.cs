using System.Globalization;
using AutoCanvas.Core;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Automations;

/// <summary>
/// Managed automations (ADR-0003): stored in folders, saved as immutable versions, and only saved
/// when the file is valid. The folder's roles decide access (ADR-0012): Viewer reads, Editor
/// changes.
/// </summary>
public sealed class AutomationService
{
    private readonly AutoCanvasDbContext database;
    private readonly IAuditLog audit;
    private readonly TimeProvider time;
    private readonly FolderAccess access;

    /// <summary>Creates the service for one unit of work.</summary>
    public AutomationService(AutoCanvasDbContext database, IAuditLog audit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        this.audit = audit;
        this.time = time;
        access = new FolderAccess(database);
    }

    /// <summary>Creates an automation in a folder (Editor) with the file as version 1.</summary>
    public async Task<UseCaseResult<AutomationView>> CreateAsync(Actor actor, Guid folderId, string name, string yaml, string? message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if ((Names.Check("name", name, Automation.MaxNameLength, "automation") ?? CheckMessage(message)) is { } invalid)
        {
            return UseCase.Failure<AutomationView>(invalid);
        }

        var folder = await access.RequireAsync(actor, folderId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<AutomationView>(denied);
        }

        var key = name.Trim();
        if (await NameTakenAsync(folderId, key, exceptId: null, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<AutomationView>(NameTaken(key));
        }

        var loaded = Load(yaml);
        if (loaded.Error is { } broken)
        {
            return UseCase.Failure<AutomationView>(broken);
        }

        var now = time.GetUtcNow();
        var automation = new Automation
        {
            Id = Guid.CreateVersion7(now),
            FolderId = folderId,
            Name = key,
            NormalizedName = IdentityNames.Normalize(key),
            Title = key,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Describe(automation, loaded.Value!);
        database.Automations.Add(automation);
        await AddVersionAsync(actor, automation, yaml, message, now, cancellationToken).ConfigureAwait(false);
        audit.Record(Entry(AuditActions.AutomationCreated, automation, ("name", automation.Name)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(actor, automation.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One automation with the file of its newest version (Viewer).</summary>
    public async Task<UseCaseResult<AutomationView>> GetAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await FindAsync(actor, id, FolderRole.Viewer, track: false, expectedVersion: null, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<AutomationView>(denied);
        }

        var (automation, role) = found.Value;
        var yaml = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == id && v.Number == automation.CurrentVersion)
            .Select(v => v.Yaml)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        var warnings = AutomationLoader.Load(yaml).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
        return UseCase.Success(new AutomationView(
            automation.Id,
            automation.FolderId,
            automation.Name,
            automation.Title,
            automation.Description,
            automation.CurrentVersion,
            yaml,
            warnings,
            role,
            automation.CreatedAt,
            automation.UpdatedAt));
    }

    /// <summary>The automations in a folder, by name, one page at a time (Viewer on the folder).</summary>
    public async Task<UseCaseResult<Page<AutomationSummary>>> ListAsync(Actor actor, Guid folderId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        var after = NameCursor.Decode(page.Cursor);
        if ((limit.Error ?? after.Error) is { } invalid)
        {
            return UseCase.Failure<Page<AutomationSummary>>(invalid);
        }

        var folder = await access.RequireAsync(actor, folderId, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<Page<AutomationSummary>>(denied);
        }

        var query = database.Automations.AsNoTracking().Where(a => a.FolderId == folderId);
        if (after.Value is { } last)
        {
            query = query.Where(a => a.NormalizedName.CompareTo(last) > 0);
        }

        var rows = await query.OrderBy(a => a.NormalizedName).Take(limit.Value + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Take(limit.Value).Select(a => new AutomationSummary(a.Id, a.Name, a.Title, a.Description, a.CurrentVersion, a.UpdatedAt)).ToList();
        var next = rows.Count > limit.Value ? NameCursor.Encode(rows[limit.Value - 1].NormalizedName) : null;
        return UseCase.Success(new Page<AutomationSummary>(items, next));
    }

    /// <summary>Saves a changed file as a new version (Editor). An unchanged file is refused.</summary>
    public async Task<UseCaseResult<AutomationView>> SaveAsync(Actor actor, Guid id, string yaml, string? message, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (CheckMessage(message) is { } invalid)
        {
            return UseCase.Failure<AutomationView>(invalid);
        }

        var found = await FindAsync(actor, id, FolderRole.Editor, track: true, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<AutomationView>(denied);
        }

        var automation = found.Value.Automation;
        var current = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == id && v.Number == automation.CurrentVersion)
            .Select(v => v.Yaml)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(current, yaml, StringComparison.Ordinal))
        {
            return UseCase.Failure<AutomationView>(UseCaseError.Invalid("yaml", $"Nothing changed: this is the content of version {automation.CurrentVersion}."));
        }

        var loaded = Load(yaml);
        if (loaded.Error is { } broken)
        {
            return UseCase.Failure<AutomationView>(broken);
        }

        var now = time.GetUtcNow();
        Describe(automation, loaded.Value!);
        automation.UpdatedAt = now;
        await AddVersionAsync(actor, automation, yaml, message, now, cancellationToken).ConfigureAwait(false);
        audit.Record(Entry(AuditActions.AutomationSaved, automation, ("version", automation.CurrentVersion), ("message", message)));
        return await SaveChangesAsync(actor, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes the automation's key in its folder (Editor). The content keeps its versions.</summary>
    public async Task<UseCaseResult<AutomationView>> RenameAsync(Actor actor, Guid id, string name, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Names.Check("name", name, Automation.MaxNameLength, "automation") is { } invalid)
        {
            return UseCase.Failure<AutomationView>(invalid);
        }

        var found = await FindAsync(actor, id, FolderRole.Editor, track: true, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<AutomationView>(denied);
        }

        var automation = found.Value.Automation;
        var key = name.Trim();
        if (string.Equals(key, automation.Name, StringComparison.Ordinal))
        {
            return await GetAsync(actor, id, cancellationToken).ConfigureAwait(false);
        }

        if (await NameTakenAsync(automation.FolderId, key, automation.Id, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<AutomationView>(NameTaken(key));
        }

        var previous = automation.Name;
        var titleWasKey = string.Equals(automation.Title, previous, StringComparison.Ordinal);
        automation.Name = key;
        automation.NormalizedName = IdentityNames.Normalize(key);
        automation.Title = titleWasKey ? key : automation.Title;
        automation.UpdatedAt = time.GetUtcNow();
        audit.Record(Entry(AuditActions.AutomationRenamed, automation, ("from", previous), ("to", key)));
        return await SaveChangesAsync(actor, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves the automation to another folder (Editor on both folders).</summary>
    public async Task<UseCaseResult<AutomationView>> MoveAsync(Actor actor, Guid id, Guid folderId, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await FindAsync(actor, id, FolderRole.Editor, track: true, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<AutomationView>(denied);
        }

        var automation = found.Value.Automation;
        if (automation.FolderId == folderId)
        {
            return await GetAsync(actor, id, cancellationToken).ConfigureAwait(false);
        }

        var target = await access.RequireAsync(actor, folderId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
        if (target.Error is { } targetDenied)
        {
            return UseCase.Failure<AutomationView>(targetDenied);
        }

        if (await NameTakenAsync(folderId, automation.Name, automation.Id, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<AutomationView>(NameTaken(automation.Name));
        }

        var from = automation.FolderId;
        automation.FolderId = folderId;
        automation.UpdatedAt = time.GetUtcNow();
        audit.Record(Entry(AuditActions.AutomationMoved, automation, ("fromFolderId", from), ("toFolderId", folderId)));
        return await SaveChangesAsync(actor, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the automation with all its versions (Editor).</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Actor actor, Guid id, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await FindAsync(actor, id, FolderRole.Editor, track: true, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<Done>(denied);
        }

        var automation = found.Value.Automation;
        database.Automations.Remove(automation);
        audit.Record(Entry(AuditActions.AutomationDeleted, automation, ("name", automation.Name), ("versions", automation.CurrentVersion)));
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Done.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return UseCase.Failure<Done>(UseCaseError.Stale());
        }
    }

    /// <summary>The saved versions, newest first, one page at a time (Viewer).</summary>
    public async Task<UseCaseResult<Page<AutomationVersionSummary>>> ListVersionsAsync(Actor actor, Guid id, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        if (limit.Error is { } badLimit)
        {
            return UseCase.Failure<Page<AutomationVersionSummary>>(badLimit);
        }

        int? before = null;
        if (page.Cursor is { } cursor)
        {
            if (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return UseCase.Failure<Page<AutomationVersionSummary>>(UseCaseError.Invalid("cursor", "cursor is not a value from a previous page."));
            }

            before = number;
        }

        var found = await FindAsync(actor, id, FolderRole.Viewer, track: false, expectedVersion: null, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<Page<AutomationVersionSummary>>(denied);
        }

        var rows = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == id && (before == null || v.Number < before))
            .OrderByDescending(v => v.Number)
            .Take(limit.Value + 1)
            .Select(v => new AutomationVersionSummary(v.Number, v.Message, v.CreatedAt, v.CreatedById, v.CreatedByName))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var next = rows.Count > limit.Value ? rows[limit.Value - 1].Number.ToString(CultureInfo.InvariantCulture) : null;
        return UseCase.Success(new Page<AutomationVersionSummary>([.. rows.Take(limit.Value)], next));
    }

    /// <summary>One saved version with its file (Viewer).</summary>
    public async Task<UseCaseResult<AutomationVersionView>> GetVersionAsync(Actor actor, Guid id, int number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await FindAsync(actor, id, FolderRole.Viewer, track: false, expectedVersion: null, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<AutomationVersionView>(denied);
        }

        var version = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == id && v.Number == number)
            .Select(v => new AutomationVersionView(v.Number, v.Message, v.CreatedAt, v.CreatedById, v.CreatedByName, v.Yaml))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return version is null
            ? UseCase.Failure<AutomationVersionView>(UseCaseError.NotFound($"The automation has no version {number}."))
            : UseCase.Success(version);
    }

    private static UseCaseError AutomationNotFound => UseCaseError.NotFound("The automation does not exist, or you may not see it.");

    private static UseCaseError NameTaken(string name) =>
        UseCaseError.Conflict($"There is an automation named '{name}' in this folder already.", "name");

    private static UseCaseError? CheckMessage(string? message) =>
        message is not null && message.Length > AutomationVersion.MaxMessageLength
            ? UseCaseError.Invalid("message", $"The message must have at most {AutomationVersion.MaxMessageLength} characters.")
            : null;

    /// <summary>Parses and validates the file; every problem comes back with the error.</summary>
    private static UseCaseResult<Core.Model.AutomationDefinition> Load(string? yaml)
    {
        var loaded = AutomationLoader.Load(yaml ?? string.Empty);
        if (loaded.Definition is { } definition)
        {
            return UseCase.Success(definition);
        }

        var errors = loaded.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        return UseCase.Failure<Core.Model.AutomationDefinition>(new UseCaseError(
            UseCaseErrorKind.Validation,
            errors == 1 ? "The automation file has a problem; see diagnostics." : $"The automation file has {errors} problems; see diagnostics.",
            "yaml",
            loaded.Diagnostics));
    }

    private static void Describe(Automation automation, Core.Model.AutomationDefinition definition)
    {
        automation.Title = Cut(definition.Name?.Trim() is { Length: > 0 } title ? title : automation.Name, Automation.MaxTitleLength)!;
        automation.Description = Cut(string.IsNullOrWhiteSpace(definition.Description) ? null : definition.Description.Trim(), Automation.MaxDescriptionLength);
    }

    private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

    private static AuditEntry Entry(string action, Automation automation, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "automation",
        TargetId = automation.Id.ToString(),
        FolderId = automation.FolderId,
        Details = AuditEntry.DetailsOf(details),
    };

    /// <summary>
    /// The automation if the actor has <paramref name="needed"/> on its folder and, when given, it is
    /// still at <paramref name="expectedVersion"/>. One the actor may not see is reported as not found.
    /// </summary>
    private async Task<UseCaseResult<(Automation Automation, FolderRole Role)>> FindAsync(
        Actor actor, Guid id, FolderRole needed, bool track, int? expectedVersion, CancellationToken cancellationToken)
    {
        var automations = track ? database.Automations : database.Automations.AsNoTracking();
        var automation = await automations.FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);
        if (automation is null)
        {
            return UseCase.Failure<(Automation, FolderRole)>(AutomationNotFound);
        }

        var folder = await access.RequireAsync(actor, automation.FolderId, needed, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } error)
        {
            return UseCase.Failure<(Automation, FolderRole)>(error.Kind == UseCaseErrorKind.NotFound ? AutomationNotFound : error);
        }

        return expectedVersion is { } expected && expected != automation.CurrentVersion
            ? UseCase.Failure<(Automation, FolderRole)>(UseCaseError.Stale())
            : UseCase.Success((automation, folder.Value!.Role));
    }

    private async Task<bool> NameTakenAsync(Guid folderId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = IdentityNames.Normalize(name);
        return await database.Automations.AnyAsync(a => a.FolderId == folderId && a.NormalizedName == normalized && a.Id != exceptId, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddVersionAsync(Actor actor, Automation automation, string yaml, string? message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var author = await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstAsync(cancellationToken).ConfigureAwait(false);
        automation.CurrentVersion++;
        database.AutomationVersions.Add(new AutomationVersion
        {
            Id = Guid.CreateVersion7(now),
            AutomationId = automation.Id,
            Number = automation.CurrentVersion,
            Yaml = yaml,
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            CreatedAt = now,
            CreatedById = actor.UserId,
            CreatedByName = author,
        });
    }

    private async Task<UseCaseResult<AutomationView>> SaveChangesAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UseCase.Failure<AutomationView>(UseCaseError.Stale());
        }

        return await GetAsync(actor, id, cancellationToken).ConfigureAwait(false);
    }
}
