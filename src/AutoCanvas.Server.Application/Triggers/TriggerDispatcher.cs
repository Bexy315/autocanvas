using System.Globalization;
using System.Text.Json;
using AutoCanvas.Core;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Triggers;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Triggers;

/// <summary>
/// Starts the runs that schedules and finished runs call for. Each due schedule entry and each
/// finished run is taken with a conditional update first, so that of several servers exactly one
/// starts a run for it; times missed while no server ran are not made up for.
/// </summary>
public sealed class TriggerDispatcher
{
    /// <summary>How long a chain of automations starting each other may get.</summary>
    public const int MaxChain = 10;

    /// <summary>Entries and runs handled per round; the rest waits for the next.</summary>
    public const int BatchSize = 50;

    private readonly AutoCanvasDbContext database;
    private readonly TimeProvider time;
    private readonly RunStarter starter;

    /// <summary>Creates the dispatcher for one unit of work.</summary>
    public TriggerDispatcher(AutoCanvasDbContext database, IJobSignal signal, ConfigurationService configuration, IAuditLog audit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        this.time = time;
        starter = new RunStarter(database, signal, audit, new RunEngine(database, configuration, time), time);
    }

    /// <summary>Starts a run for every schedule entry that is due.</summary>
    public async Task<TriggerRound> StartScheduledAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await database.AutomationSchedules.AsNoTracking()
            .Where(s => s.NextAt <= now)
            .OrderBy(s => s.NextAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var round = new TriggerRound();
        foreach (var schedule in due)
        {
            var next = ScheduleTimes.Next(schedule, now) ?? DateTimeOffset.MaxValue;
            var taken = await database.AutomationSchedules
                .Where(s => s.Id == schedule.Id && s.NextAt == schedule.NextAt)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.NextAt, next).SetProperty(s => s.LastAt, now), cancellationToken).ConfigureAwait(false);
            if (taken == 0)
            {
                continue;
            }

            var data = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["schedule"] = ScheduleTimes.Describe(schedule),
                ["scheduled_at"] = schedule.NextAt.ToString("O", CultureInfo.InvariantCulture),
            });
            await StartAsync(schedule.AutomationId, new RunStart(RunTriggerKind.Schedule, null, "schedule", data), round, cancellationToken).ConfigureAwait(false);
        }

        return round;
    }

    /// <summary>Starts the automations that follow runs that finished.</summary>
    public async Task<TriggerRound> StartFollowersAsync(CancellationToken cancellationToken)
    {
        var finished = await database.Runs.AsNoTracking()
            .Where(r => !r.CompletionHandled && r.State != RunState.Running)
            .OrderBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var round = new TriggerRound();
        foreach (var run in finished)
        {
            var taken = await database.Runs
                .Where(r => r.Id == run.Id && !r.CompletionHandled)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.CompletionHandled, true), cancellationToken).ConfigureAwait(false);
            if (taken == 1 && run.AutomationId is { } automationId)
            {
                await FollowAsync(run, automationId, round, cancellationToken).ConfigureAwait(false);
            }
        }

        return round;
    }

    private static string Result(RunState state) => state switch
    {
        RunState.Succeeded => "success",
        RunState.Failed => "failure",
        _ => "cancelled",
    };

    /// <summary>How many automations started each other before this run.</summary>
    private static int Depth(Run run)
    {
        if (run.Trigger != RunTriggerKind.AutomationCompleted || run.TriggerData is null)
        {
            return 0;
        }

        using var document = JsonDocument.Parse(run.TriggerData);
        return document.RootElement.TryGetProperty("depth", out var depth) && int.TryParse(depth.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private async Task FollowAsync(Run run, Guid automationId, TriggerRound round, CancellationToken cancellationToken)
    {
        var automation = await database.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == automationId, cancellationToken).ConfigureAwait(false);
        if (automation is null)
        {
            return;
        }

        var result = Result(run.State);
        var followers = await database.CompletionTriggers.AsNoTracking()
            .Where(c => c.TargetFolderId == automation.FolderId && c.TargetName == automation.NormalizedName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var matching = followers.Where(c => c.On == CompletionOutcome.Always
            || (c.On == CompletionOutcome.Success && run.State == RunState.Succeeded)
            || (c.On == CompletionOutcome.Failure && run.State == RunState.Failed)).ToList();
        if (matching.Count == 0)
        {
            return;
        }

        var depth = Depth(run) + 1;
        if (depth > MaxChain)
        {
            round.Skip($"Run #{run.Number} of '{automation.Name}' ends a chain of {MaxChain} automations starting each other; its followers did not start.");
            return;
        }

        var data = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["automation"] = automation.Name,
            ["run_id"] = run.Id.ToString(),
            ["run_number"] = run.Number.ToString(CultureInfo.InvariantCulture),
            ["result"] = result,
            ["depth"] = depth.ToString(CultureInfo.InvariantCulture),
        });
        foreach (var follower in matching)
        {
            await StartAsync(follower.AutomationId, new RunStart(RunTriggerKind.AutomationCompleted, null, Cut($"{automation.Name} #{run.Number}"), data), round, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Cut(string name) => name.Length <= Run.MaxTriggeredByNameLength ? name : name[..Run.MaxTriggeredByNameLength];

    private async Task StartAsync(Guid automationId, RunStart start, TriggerRound round, CancellationToken cancellationToken)
    {
        var automation = await database.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == automationId, cancellationToken).ConfigureAwait(false);
        if (automation is null)
        {
            return;
        }

        var yaml = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == automationId && v.Number == automation.CurrentVersion)
            .Select(v => v.Yaml)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        var started = await starter.StartAsync(automation, yaml, AutomationLoader.Load(yaml).Definition!, start, cancellationToken).ConfigureAwait(false);
        if (started.Error is { } refused)
        {
            database.ChangeTracker.Clear();
            round.Skip($"'{automation.Name}' did not start ({RunValues.TriggerEvent(start.Trigger)}): {refused.Message}");
            return;
        }

        round.Start(Started(automation, started.Value!));
    }

    private static string Started(Automation automation, Run run) => $"'{automation.Name}' #{run.Number}";
}
