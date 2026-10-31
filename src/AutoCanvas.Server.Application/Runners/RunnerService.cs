using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runners;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Runners;

/// <summary>Administration of registered runners: list, rename, pause and delete them.</summary>
public sealed class RunnerService(AutoCanvasDbContext database, IAuditLog audit, IOptions<RunnerOptions> options, TimeProvider time)
{
    /// <summary>Runners ordered by name.</summary>
    public async Task<UseCaseResult<Page<RunnerView>>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        var after = NameCursor.Decode(page.Cursor);
        if ((limit.Error ?? after.Error) is { } invalid)
        {
            return UseCase.Failure<Page<RunnerView>>(invalid);
        }

        var query = database.Runners.AsNoTracking();
        if (after.Value is { } last)
        {
            query = query.Where(r => r.NormalizedName.CompareTo(last) > 0);
        }

        var runners = await query.OrderBy(r => r.NormalizedName).Take(limit.Value + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var since = OnlineSince();
        var items = runners.Take(limit.Value).Select(r => RunnerView.From(r, since)).ToList();
        var next = runners.Count > limit.Value ? NameCursor.Encode(runners[limit.Value - 1].NormalizedName) : null;
        return UseCase.Success(new Page<RunnerView>(items, next));
    }

    /// <summary>One runner.</summary>
    public async Task<UseCaseResult<RunnerView>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var runner = await database.Runners.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        return runner is null ? UseCase.Failure<RunnerView>(NotFound) : UseCase.Success(RunnerView.From(runner, OnlineSince()));
    }

    /// <summary>
    /// Renames, pauses or resumes a runner. A paused runner keeps its connection and finishes the
    /// jobs it has, but is offered no new ones.
    /// </summary>
    public async Task<UseCaseResult<RunnerView>> UpdateAsync(Guid id, RunnerChanges changes, int? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Name is not null && RunnerRegistrationService.CheckName(changes.Name) is { } invalid)
        {
            return UseCase.Failure<RunnerView>(invalid);
        }

        var runner = await database.Runners.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (runner is null)
        {
            return UseCase.Failure<RunnerView>(NotFound);
        }

        if (expectedVersion is { } expected && expected != runner.Version)
        {
            return UseCase.Failure<RunnerView>(UseCaseError.Stale());
        }

        var changed = new List<string>();
        if (changes.Name?.Trim() is { } name && !string.Equals(name, runner.Name, StringComparison.Ordinal))
        {
            var normalized = name.ToUpperInvariant();
            if (await database.Runners.AnyAsync(r => r.Id != id && r.NormalizedName == normalized, cancellationToken).ConfigureAwait(false))
            {
                return UseCase.Failure<RunnerView>(RunnerRegistrationService.NameTaken);
            }

            runner.Name = name;
            runner.NormalizedName = normalized;
            changed.Add("name");
        }

        if (changes.IsPaused is { } paused && paused != runner.IsPaused)
        {
            runner.IsPaused = paused;
            changed.Add("isPaused");
        }

        if (changed.Count > 0)
        {
            runner.Version++;
            audit.Record(Entry(AuditActions.RunnerUpdated, runner, ("changed", changed), ("isPaused", runner.IsPaused)));
            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return UseCase.Failure<RunnerView>(UseCaseError.Stale());
            }
            catch (DbUpdateException)
            {
                // Another runner took the name since the check above.
                return UseCase.Failure<RunnerView>(RunnerRegistrationService.NameTaken);
            }
        }

        return UseCase.Success(RunnerView.From(runner, OnlineSince()));
    }

    /// <summary>
    /// Deletes a runner and with it its credential: it is refused from now on and told not to
    /// reconnect. Jobs it still held are given up: the sweeper offers them again or fails them.
    /// </summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Guid id, int? expectedVersion, CancellationToken cancellationToken)
    {
        var runner = await database.Runners.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (runner is null)
        {
            return UseCase.Failure<Done>(NotFound);
        }

        if (expectedVersion is { } expected && expected != runner.Version)
        {
            return UseCase.Failure<Done>(UseCaseError.Stale());
        }

        database.Runners.Remove(runner);
        audit.Record(Entry(AuditActions.RunnerDeleted, runner));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Ending the leases now hands the attempts to the sweeper, which knows what follows for
        // offered and for running ones.
        var now = time.GetUtcNow();
        await database.JobAttempts
            .Where(a => a.RunnerId == id && (a.Status == AttemptStatus.Offered || a.Status == AttemptStatus.Running))
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.LeaseExpiresAt, now), cancellationToken)
            .ConfigureAwait(false);
        return Done.Result;
    }

    private DateTimeOffset OnlineSince() => time.GetUtcNow() - (2 * options.Value.HeartbeatInterval);

    private static UseCaseError NotFound => UseCaseError.NotFound("The runner does not exist.");

    private static AuditEntry Entry(string action, Runner runner, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "runner",
        TargetId = runner.Id.ToString(),
        Details = AuditEntry.DetailsOf([("name", runner.Name), .. details]),
    };
}
