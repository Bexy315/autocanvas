using AutoCanvas.Core;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Triggers;

/// <summary>Reading what starts an automation's runs. Needs Viewer on its folder.</summary>
public sealed class AutomationTriggerService(AutoCanvasDbContext database)
{
    private readonly FolderAccess access = new(database);

    /// <summary>The automation's triggers with the next times of its schedules.</summary>
    public async Task<UseCaseResult<AutomationTriggersView>> GetAsync(Actor actor, Guid automationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var automation = await database.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == automationId, cancellationToken).ConfigureAwait(false);
        if (automation is null || (await access.RequireAsync(actor, automation.FolderId, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false)).Error is not null)
        {
            return UseCase.Failure<AutomationTriggersView>(UseCaseError.NotFound("The automation does not exist, or you may not see it."));
        }

        var yaml = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == automationId && v.Number == automation.CurrentVersion)
            .Select(v => v.Yaml)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        var triggers = AutomationLoader.Load(yaml).Definition!.Triggers;
        var schedules = await database.AutomationSchedules.AsNoTracking()
            .Where(s => s.AutomationId == automationId)
            .OrderBy(s => s.Position)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var completion = await database.CompletionTriggers.AsNoTracking().FirstOrDefaultAsync(c => c.AutomationId == automationId, cancellationToken).ConfigureAwait(false);
        CompletionView? follows = null;
        if (completion is not null)
        {
            var name = await database.Automations.AsNoTracking()
                .Where(a => a.FolderId == completion.TargetFolderId && a.NormalizedName == completion.TargetName)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            follows = new CompletionView(completion.TargetFolderId, name, completion.On);
        }

        return UseCase.Success(new AutomationTriggersView(
            triggers.Manual,
            triggers.Webhook,
            [.. schedules.Select(s => new ScheduleView(s.Position, ScheduleTimes.Describe(s), s.TimeZone, s.NextAt == DateTimeOffset.MaxValue ? null : s.NextAt, s.LastAt))],
            follows));
    }
}
