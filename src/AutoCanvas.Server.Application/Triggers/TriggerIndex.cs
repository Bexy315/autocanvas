using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Triggers;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Triggers;

/// <summary>
/// Keeps the triggers of an automation's current version where the trigger service finds them: its
/// schedule entries with when each fires next, and the automation its <c>automation_completed</c>
/// follows. What the file names is checked first — cron expressions and time zones (which Core
/// cannot check without a cron library), and that the followed automation exists and the saving
/// person may see it.
/// </summary>
internal sealed class TriggerIndex(AutoCanvasDbContext database, FolderAccess access, TimeProvider time)
{
    /// <summary>The problems of the triggers, as diagnostics of the file; empty when they can be used.</summary>
    public async Task<(TriggerPlan Plan, IReadOnlyList<Diagnostic> Problems)> PrepareAsync(Actor actor, Guid folderId, Guid? automationId, Core.Model.Triggers triggers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        var problems = new List<Diagnostic>();
        var schedules = new List<AutomationSchedule>();
        for (var i = 0; i < triggers.Schedules.Count; i++)
        {
            var path = $"triggers.schedule[{i}]";
            switch (triggers.Schedules[i])
            {
                case CronSchedule cron:
                    var problem = ScheduleTimes.CheckCron(cron.Expression);
                    Report(problems, $"{path}.cron", problem);
                    var zone = cron.TimeZone is { } name ? ScheduleTimes.CheckTimeZone(name) : null;
                    Report(problems, $"{path}.timezone", zone);
                    if (problem is null && zone is null)
                    {
                        schedules.Add(Entry(automationId, i, cron.Expression, cron.TimeZone, null));
                    }

                    break;
                case IntervalSchedule interval:
                    schedules.Add(Entry(automationId, i, null, null, (long)interval.Interval.TotalSeconds));
                    break;
            }
        }

        CompletionTrigger? completion = null;
        if (triggers.AutomationCompleted is { } follows)
        {
            var (target, problem) = await ResolveAsync(actor, folderId, automationId, follows.Automation, cancellationToken).ConfigureAwait(false);
            Report(problems, "triggers.automation_completed.automation", problem);
            completion = target is { } found
                ? new CompletionTrigger
                {
                    AutomationId = automationId ?? Guid.Empty,
                    TargetFolderId = found.FolderId,
                    TargetName = found.Name,
                    On = follows.On switch
                    {
                        OutcomeFilter.Failure => CompletionOutcome.Failure,
                        OutcomeFilter.Always => CompletionOutcome.Always,
                        _ => CompletionOutcome.Success,
                    },
                }
                : null;
        }

        return (new TriggerPlan(schedules, completion), problems);
    }

    /// <summary>
    /// Stores the plan for the automation, in the caller's unit of work. An entry that stays the
    /// same keeps its next time, so that saving a file does not shift its schedule.
    /// </summary>
    public async Task ApplyAsync(Guid automationId, TriggerPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var now = time.GetUtcNow();
        var existing = await database.AutomationSchedules.Where(s => s.AutomationId == automationId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var wanted in plan.Schedules)
        {
            var kept = existing.FirstOrDefault(s => Same(s, wanted));
            if (kept is not null)
            {
                existing.Remove(kept);
                kept.Position = wanted.Position;
                continue;
            }

            database.AutomationSchedules.Add(new AutomationSchedule
            {
                Id = Guid.CreateVersion7(now),
                AutomationId = automationId,
                Position = wanted.Position,
                Cron = wanted.Cron,
                TimeZone = wanted.TimeZone,
                IntervalSeconds = wanted.IntervalSeconds,
                NextAt = ScheduleTimes.Next(wanted, now) ?? DateTimeOffset.MaxValue,
            });
        }

        database.AutomationSchedules.RemoveRange(existing);
        var completion = await database.CompletionTriggers.FirstOrDefaultAsync(c => c.AutomationId == automationId, cancellationToken).ConfigureAwait(false);
        if (plan.Completion is null)
        {
            if (completion is not null)
            {
                database.CompletionTriggers.Remove(completion);
            }

            return;
        }

        if (completion is null)
        {
            database.CompletionTriggers.Add(new CompletionTrigger { AutomationId = automationId, TargetFolderId = plan.Completion.TargetFolderId, TargetName = plan.Completion.TargetName, On = plan.Completion.On });
            return;
        }

        completion.TargetFolderId = plan.Completion.TargetFolderId;
        completion.TargetName = plan.Completion.TargetName;
        completion.On = plan.Completion.On;
    }

    private static AutomationSchedule Entry(Guid? automationId, int position, string? cron, string? timeZone, long? seconds) => new()
    {
        Id = Guid.Empty,
        AutomationId = automationId ?? Guid.Empty,
        Position = position,
        Cron = cron,
        TimeZone = timeZone,
        IntervalSeconds = seconds,
        NextAt = DateTimeOffset.MaxValue,
    };

    private static bool Same(AutomationSchedule a, AutomationSchedule b) =>
        string.Equals(a.Cron, b.Cron, StringComparison.Ordinal) && string.Equals(a.TimeZone, b.TimeZone, StringComparison.Ordinal) && a.IntervalSeconds == b.IntervalSeconds;

    private static void Report(List<Diagnostic> problems, string path, string? message)
    {
        if (message is not null)
        {
            problems.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCodes.InvalidValue, message, path, null));
        }
    }

    /// <summary>
    /// The folder and normalized name of the followed automation: a name alone is in the
    /// automation's own folder, <c>a/b/name</c> a path of folder names from the top.
    /// </summary>
    private async Task<((Guid FolderId, string Name)? Target, string? Problem)> ResolveAsync(Actor actor, Guid folderId, Guid? automationId, string reference, CancellationToken cancellationToken)
    {
        var parts = reference.Split('/', StringSplitOptions.TrimEntries);
        var name = IdentityNames.Normalize(parts[^1]);
        var folder = folderId;
        if (parts.Length > 1)
        {
            Guid? parent = null;
            foreach (var part in parts[..^1])
            {
                var normalized = IdentityNames.Normalize(part);
                parent = await database.Folders.AsNoTracking()
                    .Where(f => f.ParentId == parent && f.NormalizedName == normalized)
                    .Select(f => (Guid?)f.Id)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    return (null, $"There is no folder '{string.Join('/', parts[..^1])}' for '{reference}'.");
                }
            }

            folder = parent!.Value;
        }

        // Also an automation made in this unit of work and not saved yet: a repository's sync makes
        // all its new automations before it saves them.
        var target = database.Automations.Local
            .Where(a => a.FolderId == folder && string.Equals(a.NormalizedName, name, StringComparison.Ordinal))
            .Select(a => (Guid?)a.Id)
            .FirstOrDefault()
            ?? await database.Automations.AsNoTracking()
                .Where(a => a.FolderId == folder && a.NormalizedName == name)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var visible = target is not null && (await access.RequireAsync(actor, folder, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false)).Error is null;
        if (!visible)
        {
            return (null, $"There is no automation '{reference}' you may see. A name alone means one in the same folder; give a path such as team/build for another.");
        }

        return target == automationId
            ? (null, "An automation cannot start when it finishes itself.")
            : ((folder, name), null);
    }
}
