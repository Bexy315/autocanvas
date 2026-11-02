using System.Globalization;
using AutoCanvas.Core;
using AutoCanvas.Core.Model;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Starting, cancelling and reading runs (ADR-0015). The automation's folder decides access
/// (ADR-0012): Operators start and cancel runs, Viewers read them.
/// </summary>
public sealed class RunService
{
    private readonly AutoCanvasDbContext database;
    private readonly IJobSignal signal;
    private readonly IAuditLog audit;
    private readonly TimeProvider time;
    private readonly FolderAccess access;
    private readonly RunEngine engine;
    private readonly RunStarter starter;

    /// <summary>Creates the service for one unit of work.</summary>
    public RunService(AutoCanvasDbContext database, IJobSignal signal, ConfigurationService configuration, IAuditLog audit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        this.signal = signal;
        this.audit = audit;
        this.time = time;
        access = new FolderAccess(database);
        engine = new RunEngine(database, configuration, time);
        starter = new RunStarter(database, signal, audit, engine, time);
    }

    /// <summary>
    /// Starts a run of the automation's current version by hand (Operator). The automation must
    /// allow it (<c>triggers.manual</c>); with <c>concurrency: single</c> no other run may be active.
    /// </summary>
    public async Task<UseCaseResult<RunView>> StartAsync(Actor actor, Guid automationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return await TryStartAsync(actor, automationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cancels a run (Operator): unfinished jobs stop, finally jobs that run on cancel still run.</summary>
    public async Task<UseCaseResult<RunView>> CancelAsync(Actor actor, Guid runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await FindAsync(actor, runId, FolderRole.Operator, track: true, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<RunView>(denied);
        }

        var run = found.Value!;
        if (run.State != RunState.Running || run.CancelRequested)
        {
            return await GetAsync(actor, runId, cancellationToken).ConfigureAwait(false);
        }

        run.CancelRequested = true;
        run.CancelledByName = await UserNameAsync(actor, cancellationToken).ConfigureAwait(false);
        var jobs = await JobsAsync(run.Id, track: true, cancellationToken).ConfigureAwait(false);
        await engine.CancelAsync(run, jobs, cancellationToken).ConfigureAwait(false);
        audit.Record(Entry(AuditActions.RunCancelled, run));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Finally jobs that run on cancel may have been queued.
        await signal.NotifyAsync(database, cancellationToken).ConfigureAwait(false);
        return await GetAsync(actor, runId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A run with its jobs (Viewer).</summary>
    public async Task<UseCaseResult<RunView>> GetAsync(Actor actor, Guid runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var found = await FindAsync(actor, runId, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<RunView>(denied);
        }

        var run = found.Value!;
        var jobs = await JobsAsync(run.Id, track: false, cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new RunView(
            run.Id,
            run.Number,
            run.AutomationId,
            run.AutomationName,
            run.AutomationVersion,
            run.FolderId,
            run.State,
            run.Trigger,
            run.TriggeredByName,
            run.CancelRequested,
            run.CancelledByName,
            run.CreatedAt,
            run.FinishedAt,
            [.. jobs.Select(j => new RunJobView(j.Key, j.Name, j.Status, j.ActivatedCase, j.Reason, j.AttemptCount, j.StartedAt, j.FinishedAt))],
            run.Yaml));
    }

    /// <summary>The automation's runs, newest first, one page at a time (Viewer).</summary>
    public async Task<UseCaseResult<Page<RunSummary>>> ListAsync(Actor actor, Guid automationId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        if (limit.Error is { } badLimit)
        {
            return UseCase.Failure<Page<RunSummary>>(badLimit);
        }

        int? before = null;
        if (page.Cursor is { } cursor)
        {
            if (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return UseCase.Failure<Page<RunSummary>>(UseCaseError.Invalid("cursor", "cursor is not a value from a previous page."));
            }

            before = number;
        }

        var folderId = await database.Automations.AsNoTracking().Where(a => a.Id == automationId).Select(a => (Guid?)a.FolderId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (folderId is null)
        {
            return UseCase.Failure<Page<RunSummary>>(AutomationNotFound);
        }

        var folder = await access.RequireAsync(actor, folderId.Value, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<Page<RunSummary>>(denied.Kind == UseCaseErrorKind.NotFound ? AutomationNotFound : denied);
        }

        var rows = await database.Runs.AsNoTracking()
            .Where(r => r.AutomationId == automationId && (before == null || r.Number < before))
            .OrderByDescending(r => r.Number)
            .Take(limit.Value + 1)
            .Select(r => new RunSummary(r.Id, r.Number, r.AutomationId, r.AutomationName, r.FolderId, r.State, r.Trigger, r.TriggeredByName, r.CreatedAt, r.FinishedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var next = rows.Count > limit.Value ? rows[limit.Value - 1].Number.ToString(CultureInfo.InvariantCulture) : null;
        return UseCase.Success(new Page<RunSummary>([.. rows.Take(limit.Value)], next));
    }

    /// <summary>
    /// The newest runs of every automation the actor may view (Viewer on the run's folder), one page
    /// at a time, optionally only those in one state.
    /// </summary>
    public async Task<UseCaseResult<Page<RunSummary>>> ListRecentAsync(Actor actor, RunState? state, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        var after = TimeCursor.Decode(page.Cursor);
        if ((limit.Error ?? after.Error) is { } invalid)
        {
            return UseCase.Failure<Page<RunSummary>>(invalid);
        }

        var runs = database.Runs.AsNoTracking();
        var viewable = await access.ViewableIdsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (viewable is not null)
        {
            var ids = viewable.ToList();
            runs = runs.Where(r => ids.Contains(r.FolderId));
        }

        if (state is { } only)
        {
            runs = runs.Where(r => r.State == only);
        }

        var skip = 0;
        if (after.Value is { } cursor)
        {
            runs = runs.Where(r => r.CreatedAt <= cursor.At);
            skip = cursor.SameInstant;
        }

        var rows = await runs.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(limit.Value + 1)
            .Select(r => new RunSummary(r.Id, r.Number, r.AutomationId, r.AutomationName, r.FolderId, r.State, r.Trigger, r.TriggeredByName, r.CreatedAt, r.FinishedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Take(limit.Value).ToList();
        var next = rows.Count > limit.Value ? TimeCursor.After(items.Select(r => r.CreatedAt), items[^1].CreatedAt, after.Value) : null;
        return UseCase.Success(new Page<RunSummary>(items, next));
    }

    private static UseCaseError AutomationNotFound => UseCaseError.NotFound("The automation does not exist, or you may not see it.");

    private static UseCaseError RunNotFound => UseCaseError.NotFound("The run does not exist, or you may not see it.");

    private static AuditEntry Entry(string action, Run run) => new(action)
    {
        TargetType = "run",
        TargetId = run.Id.ToString(),
        FolderId = run.FolderId,
        Details = AuditEntry.DetailsOf(("automation", run.AutomationName), ("number", run.Number)),
    };

    private async Task<UseCaseResult<RunView>> TryStartAsync(Actor actor, Guid automationId, CancellationToken cancellationToken)
    {
        var automation = await database.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == automationId, cancellationToken).ConfigureAwait(false);
        if (automation is null)
        {
            return UseCase.Failure<RunView>(AutomationNotFound);
        }

        var folder = await access.RequireAsync(actor, automation.FolderId, FolderRole.Operator, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<RunView>(denied.Kind == UseCaseErrorKind.NotFound ? AutomationNotFound : denied);
        }

        var yaml = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == automationId && v.Number == automation.CurrentVersion)
            .Select(v => v.Yaml)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        var definition = AutomationLoader.Load(yaml).Definition!;
        if (!definition.Triggers.Manual)
        {
            return UseCase.Failure<RunView>(UseCaseError.Invalid("automation", "This automation cannot be started by hand. Add 'manual: true' under triggers to allow it."));
        }

        var started = await starter.StartAsync(
            automation,
            yaml,
            definition,
            new RunStart(RunTriggerKind.Manual, actor.UserId, await UserNameAsync(actor, cancellationToken).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
        if (started.Error is { } refused)
        {
            return UseCase.Failure<RunView>(refused);
        }

        var run = started.Value!;
        return await GetAsync(actor, run.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The run if the actor has <paramref name="needed"/> on its folder; otherwise reported as not found.</summary>
    private async Task<UseCaseResult<Run>> FindAsync(Actor actor, Guid runId, FolderRole needed, bool track, CancellationToken cancellationToken)
    {
        var runs = track ? database.Runs : database.Runs.AsNoTracking();
        var run = await runs.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return UseCase.Failure<Run>(RunNotFound);
        }

        var folder = await access.RequireAsync(actor, run.FolderId, needed, track: false, cancellationToken).ConfigureAwait(false);
        return folder.Error is { } error
            ? UseCase.Failure<Run>(error.Kind == UseCaseErrorKind.NotFound ? RunNotFound : error)
            : UseCase.Success(run);
    }

    private async Task<List<RunJob>> JobsAsync(Guid runId, bool track, CancellationToken cancellationToken)
    {
        var jobs = track ? database.RunJobs : database.RunJobs.AsNoTracking();
        return await jobs.Where(j => j.RunId == runId).OrderBy(j => j.Position).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> UserNameAsync(Actor actor, CancellationToken cancellationToken) =>
        await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstAsync(cancellationToken).ConfigureAwait(false);
}
