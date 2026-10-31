using AutoCanvas.Core.Model;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>Everything about one job attempt that resolving its offer or its steps needs.</summary>
/// <param name="Attempt">The attempt.</param>
/// <param name="Job">Its job in the run.</param>
/// <param name="Run">The run.</param>
/// <param name="Definition">The job as the run's automation file defines it.</param>
/// <param name="Values">What its expressions read, without step outputs.</param>
internal sealed record AttemptContext(JobAttempt Attempt, RunJob Job, Run Run, StepJob Definition, RunValues Values)
{
    /// <summary>The attempt with its run; <see langword="null"/> if it does not exist.</summary>
    /// <param name="database">The unit of work.</param>
    /// <param name="configuration">Resolves the run's variables and secrets.</param>
    /// <param name="time">The clock.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="track">Whether the attempt and its job are changed and saved afterwards.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    public static async Task<AttemptContext?> LoadAsync(AutoCanvasDbContext database, ConfigurationService configuration, TimeProvider time, Guid attemptId, bool track, CancellationToken cancellationToken)
    {
        var attempts = track ? database.JobAttempts : database.JobAttempts.AsNoTracking();
        var attempt = await attempts.FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is null)
        {
            return null;
        }

        var runId = await database.RunJobs.Where(j => j.Id == attempt.RunJobId).Select(j => j.RunId).FirstAsync(cancellationToken).ConfigureAwait(false);
        var jobs = await (track ? database.RunJobs : database.RunJobs.AsNoTracking())
            .Where(j => j.RunId == runId)
            .OrderBy(j => j.Position)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var job = jobs.First(j => j.Id == attempt.RunJobId);
        var run = await database.Runs.AsNoTracking().FirstAsync(r => r.Id == job.RunId, cancellationToken).ConfigureAwait(false);
        var automation = RunEngine.Load(run);
        var definition = (StepJob)automation.Jobs.First(j => string.Equals(j.Id, job.Key, StringComparison.Ordinal));
        var values = await new RunEngine(database, configuration, time).ValuesAsync(run, automation, jobs, cancellationToken).ConfigureAwait(false);
        return new AttemptContext(attempt, job, run, definition, values);
    }
}
