using AutoCanvas.Core.Model;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Starts a run of an automation's current version, whatever started it: a person, a schedule, a
/// webhook or another automation. Who may start it is checked by the caller; what the automation
/// allows (<c>concurrency: single</c>) is checked here.
/// </summary>
internal sealed class RunStarter(AutoCanvasDbContext database, IJobSignal signal, IAuditLog audit, RunEngine engine, TimeProvider time)
{
    /// <summary>How often a start is tried again when another start took the same run number.</summary>
    private const int NumberAttempts = 3;

    public async Task<UseCaseResult<Run>> StartAsync(Automation automation, string yaml, AutomationDefinition definition, RunStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(start);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryStartAsync(automation, yaml, definition, start, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) when (attempt < NumberAttempts)
            {
                // Another start took the same run number in the meantime; count again.
                database.ChangeTracker.Clear();
            }
        }
    }

    private async Task<UseCaseResult<Run>> TryStartAsync(Automation automation, string yaml, AutomationDefinition definition, RunStart start, CancellationToken cancellationToken)
    {
        if (definition.Concurrency == Concurrency.Exclusive
            && await database.Runs.AnyAsync(r => r.AutomationId == automation.Id && r.State == RunState.Running, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<Run>(UseCaseError.Conflict("The automation allows one run at a time (concurrency: single), and a run is still active."));
        }

        var now = time.GetUtcNow();
        var last = await database.Runs.Where(r => r.AutomationId == automation.Id).MaxAsync(r => (int?)r.Number, cancellationToken).ConfigureAwait(false);
        var run = new Run
        {
            Id = Guid.CreateVersion7(now),
            AutomationId = automation.Id,
            FolderId = automation.FolderId,
            AutomationName = automation.Name,
            AutomationVersion = automation.CurrentVersion,
            Number = (last ?? 0) + 1,
            Yaml = yaml,
            Trigger = start.Trigger,
            TriggerData = start.Data,
            TriggeredById = start.TriggeredById,
            TriggeredByName = start.TriggeredByName,
            CreatedAt = now,
        };
        database.Runs.Add(run);
        var jobs = engine.CreateJobs(run, definition);
        await engine.AdvanceAsync(run, jobs, cancellationToken).ConfigureAwait(false);
        audit.Record(new AuditEntry(AuditActions.RunStarted)
        {
            TargetType = "run",
            TargetId = run.Id.ToString(),
            FolderId = run.FolderId,
            Details = AuditEntry.DetailsOf(("automation", run.AutomationName), ("number", run.Number), ("trigger", RunValues.TriggerEvent(run.Trigger))),
        });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await signal.NotifyAsync(database, cancellationToken).ConfigureAwait(false);
        return UseCase.Success(run);
    }
}
