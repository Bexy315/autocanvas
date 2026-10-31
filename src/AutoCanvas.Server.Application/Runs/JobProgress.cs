using System.Text.Json;
using AutoCanvas.Core.Model;
using AutoCanvas.Core.Orchestration;
using AutoCanvas.Core.Secrets;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using CoreStepOutcome = AutoCanvas.Core.Orchestration.StepOutcome;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// The steps of a running attempt (ADR-0008): the server decides each next step with Core's
/// <see cref="StepPlanner"/> from the outcomes stored so far, resolves it, records what the runner
/// reports, and decides the job's result when the runner is done. Everything is stored, so any
/// server can continue an attempt after its runner reconnects. Every method acts only on an attempt
/// the runner holds and that is running; otherwise it does nothing and returns no instruction.
/// </summary>
public sealed class JobProgress(AutoCanvasDbContext database, ConfigurationService configuration, JobDispatcher dispatcher, TimeProvider time)
{
    /// <summary>
    /// The runner prepared the attempt, or failed to (<paramref name="error"/>): the first step, or
    /// <c>CompleteJob</c> when preparation failed or there is nothing to run.
    /// </summary>
    public async Task<JobInstruction?> PreparedAsync(Guid attemptId, Guid runnerId, string? error, CancellationToken cancellationToken)
    {
        if (await HeldAsync(attemptId, runnerId, cancellationToken).ConfigureAwait(false) is not { } context)
        {
            return null;
        }

        var attempt = context.Attempt;
        if (attempt.PreparedAt is not null || attempt.Error is not null)
        {
            // Said twice, after a reconnect: repeat what is due.
            return await NextAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(error))
        {
            attempt.Error = Cut(Redactor(context).Redact(error), JobAttempt.MaxErrorLength);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new CompleteJobInstruction(attemptId);
        }

        attempt.PreparedAt = time.GetUtcNow();
        return await NextAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The runner started a step.</summary>
    public async Task StepStartedAsync(Guid attemptId, Guid runnerId, int index, DateTimeOffset? startedAt, CancellationToken cancellationToken)
    {
        if (await HeldAsync(attemptId, runnerId, cancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        var step = await database.JobSteps.FirstOrDefaultAsync(s => s.AttemptId == attemptId && s.Index == index && s.Status == JobStepStatus.Sent, cancellationToken).ConfigureAwait(false);
        if (step is not null)
        {
            step.Status = JobStepStatus.Running;
            step.StartedAt = startedAt ?? time.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The runner reports how a step ended: its outcome and outputs are stored (masked again) and
    /// the next step or <c>CompleteJob</c> is returned. A report of a step that is not the current one,
    /// such as a repeated one after a reconnect, is ignored.
    /// </summary>
    public async Task<JobInstruction?> StepCompletedAsync(Guid attemptId, Guid runnerId, StepReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (await HeldAsync(attemptId, runnerId, cancellationToken).ConfigureAwait(false) is not { } context)
        {
            return null;
        }

        var step = await database.JobSteps
            .FirstOrDefaultAsync(s => s.AttemptId == attemptId && s.Index == report.Index && (s.Status == JobStepStatus.Sent || s.Status == JobStepStatus.Running), cancellationToken)
            .ConfigureAwait(false);
        if (step is null || report.Index >= context.Definition.Steps.Count)
        {
            return null;
        }

        var redactor = Redactor(context);
        var outputs = report.Outputs.ToDictionary(o => o.Key, o => redactor.Redact(o.Value), StringComparer.Ordinal);
        var outcome = report.Outcome;
        var error = report.Error;
        if (context.Definition.Steps[report.Index] is NodeStep { Outputs.Count: > 0 } node)
        {
            // A node step's own outputs are readable by its extra outputs.
            var steps = await StepOutputsAsync(attemptId, cancellationToken).ConfigureAwait(false);
            if (node.Id is not null)
            {
                steps[node.Id] = outputs;
            }

            var values = new SecretReads(context.Values.WithSteps(steps));
            foreach (var (name, text) in node.Outputs)
            {
                outputs[name] = redactor.Redact(values.Render(text));
            }
        }

        var stored = RunOutputs.Write(outputs);
        if (stored.Length > JobStep.MaxOutputsLength)
        {
            (outcome, error, stored) = (CoreStepOutcome.Errored, $"The step's outputs exceed {JobStep.MaxOutputsLength / 1024} KiB.", null);
        }

        step.Status = ToStatus(outcome);
        step.ExitCode = report.ExitCode;
        step.Outputs = stored;
        step.Error = string.IsNullOrEmpty(error) ? null : Cut(redactor.Redact(error), JobStep.MaxReasonLength);
        step.StartedAt ??= report.FinishedAt;
        step.FinishedAt = report.FinishedAt ?? time.GetUtcNow();
        return await NextAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The runner is done with the attempt: the job's result is decided from the step outcomes and
    /// the automation's rules, and the run moves on (<see cref="JobDispatcher.FinishAsync"/>).
    /// </summary>
    /// <returns>Whether the runner held such an attempt.</returns>
    public async Task<bool> FinishedAsync(Guid attemptId, Guid runnerId, IReadOnlyList<string> warnings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        if (await HeldAsync(attemptId, runnerId, cancellationToken).ConfigureAwait(false) is not { } context)
        {
            return false;
        }

        var attempt = context.Attempt;
        var steps = await database.JobSteps.Where(s => s.AttemptId == attemptId).OrderBy(s => s.Index).ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        foreach (var open in steps.Where(s => s.Status is JobStepStatus.Sent or JobStepStatus.Running))
        {
            open.Status = attempt.CancelReason is null ? JobStepStatus.Errored : JobStepStatus.Cancelled;
            open.Error = attempt.CancelReason ?? "The runner finished the job while this step was not done.";
            open.FinishedAt = now;
        }

        if (warnings.Count > 0)
        {
            var redactor = Redactor(context);
            var json = JsonSerializer.Serialize(warnings.Select(w => redactor.Redact(w)).ToArray());
            attempt.Warnings = json.Length > JobAttempt.MaxWarningsLength ? JsonSerializer.Serialize(TooManyWarnings) : json;
        }

        var (outcome, error) = Result(context, steps);
        return await dispatcher.FinishAsync(attemptId, runnerId, outcome, error, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What is due for an attempt the runner still works on after reconnecting: the step it was sent
    /// but may not have received, the next step if the server had not decided it yet, or
    /// <c>CompleteJob</c>. Nothing while it prepares or runs a step.
    /// </summary>
    public async Task<JobInstruction?> PendingAsync(Guid attemptId, Guid runnerId, CancellationToken cancellationToken)
    {
        if (await HeldAsync(attemptId, runnerId, cancellationToken).ConfigureAwait(false) is not { } context
            || context.Attempt.CancelReason is not null
            || (context.Attempt.PreparedAt is null && context.Attempt.Error is null))
        {
            return null;
        }

        return await NextAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Running attempts of the runner it is to stop (<c>CancelJob</c>), with the reason.</summary>
    public async Task<IReadOnlyList<(Guid AttemptId, string Reason)>> CancellationsAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var stopping = await database.JobAttempts.AsNoTracking()
            .Where(a => a.RunnerId == runnerId && a.Status == AttemptStatus.Running && a.CancelReason != null)
            .Select(a => new { a.Id, a.CancelReason })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. stopping.Select(a => (a.Id, a.CancelReason!))];
    }

    /// <summary>
    /// Decides and stores what follows the steps so far: skipped steps are recorded, the next step to
    /// run is recorded as sent and returned; when the job is decided, its outputs are stored.
    /// </summary>
    private async Task<JobInstruction?> NextAsync(AttemptContext context, CancellationToken cancellationToken)
    {
        var attempt = context.Attempt;
        var steps = await database.JobSteps.Where(s => s.AttemptId == attempt.Id).OrderBy(s => s.Index).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (attempt.Error is not null && attempt.PreparedAt is null)
        {
            return new CompleteJobInstruction(attempt.Id);
        }

        if (steps.FirstOrDefault(s => s.Status == JobStepStatus.Sent) is { } sent)
        {
            return new RunStepInstruction(attempt.Id, await ResolveAsync(context, steps, sent.Index, cancellationToken).ConfigureAwait(false));
        }

        if (steps.Any(s => s.Status == JobStepStatus.Running))
        {
            return null;
        }

        var outcomes = steps.Select(s => ToOutcome(s.Status)).ToList();
        while (attempt.CancelReason is null)
        {
            var values = context.Values.WithSteps(Outputs(steps));
            switch (StepPlanner.Next(context.Definition, outcomes, values))
            {
                case SkipStep skip:
                    steps.Add(Record(context, skip.Index, JobStepStatus.Skipped, skip.Reason));
                    outcomes.Add(CoreStepOutcome.Skipped);
                    continue;
                case Core.Orchestration.RunStep run:
                    var resolved = await ResolveAsync(context, steps, run.Index, cancellationToken).ConfigureAwait(false);
                    Record(context, run.Index, JobStepStatus.Sent, null);
                    await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return new RunStepInstruction(attempt.Id, resolved);
                default:
                    StoreJobOutputs(context, steps);
                    await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return new CompleteJobInstruction(attempt.Id);
            }
        }

        // A cancelled attempt gets CancelJob from its session, not more steps.
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async Task<ResolvedStep> ResolveAsync(AttemptContext context, IReadOnlyList<JobStep> steps, int index, CancellationToken cancellationToken)
    {
        var capabilities = await database.Runners.AsNoTracking()
            .Where(r => r.Id == context.Attempt.RunnerId)
            .Select(r => r.Capabilities)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var shells = capabilities is null ? [] : RunnerCapabilities.Read(capabilities).Shells;
        return StepResolver.Resolve(context.Definition, index, context.Values.WithSteps(Outputs(steps)), shells);
    }

    private JobStep Record(AttemptContext context, int index, JobStepStatus status, string? reason)
    {
        var definition = context.Definition.Steps[index];
        var step = new JobStep
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            AttemptId = context.Attempt.Id,
            Index = index,
            StepId = definition.Id,
            Name = Cut(StepResolver.NameOf(definition, index), JobStep.MaxNameLength),
            Status = status,
            Reason = reason is null ? null : Cut(reason, JobStep.MaxReasonLength),
        };
        database.JobSteps.Add(step);
        return step;
    }

    /// <summary>The job's <c>outputs</c>, rendered with all its steps' outputs and masked.</summary>
    private static void StoreJobOutputs(AttemptContext context, IReadOnlyList<JobStep> steps)
    {
        if (context.Definition.Outputs.Count == 0)
        {
            return;
        }

        var redactor = Redactor(context);
        var values = new SecretReads(context.Values.WithSteps(Outputs(steps)));
        var outputs = context.Definition.Outputs.ToDictionary(o => o.Key, o => redactor.Redact(values.Render(o.Value)), StringComparer.Ordinal);
        var json = RunOutputs.Write(outputs);
        context.Job.Outputs = json.Length > RunJob.MaxOutputsLength ? null : json;
    }

    /// <summary>The attempt's result once the runner is done.</summary>
    private static (AttemptOutcome Outcome, string? Error) Result(AttemptContext context, List<JobStep> steps)
    {
        var attempt = context.Attempt;
        if (attempt.TimedOut)
        {
            return (AttemptOutcome.Failed, $"The job ran longer than its time limit of {attempt.TimeoutMinutes} minutes.");
        }

        if (attempt.CancelReason is { } reason)
        {
            return (AttemptOutcome.Cancelled, reason);
        }

        if (attempt.PreparedAt is null)
        {
            return (AttemptOutcome.Failed, attempt.Error ?? "The runner finished the job before preparing it.");
        }

        if (steps.Count < context.Definition.Steps.Count)
        {
            return (AttemptOutcome.Failed, "The runner finished the job before all its steps ran.");
        }

        return StepPlanner.JobResult(context.Definition, [.. steps.Select(s => ToOutcome(s.Status))]) switch
        {
            JobStatus.Succeeded => (AttemptOutcome.Succeeded, null),
            JobStatus.Cancelled => (AttemptOutcome.Cancelled, null),
            _ => (AttemptOutcome.Failed, steps.FirstOrDefault(s => s.Error is not null)?.Error),
        };
    }

    /// <summary>The attempt with its run, if the runner holds it and it is running; tracked for changes.</summary>
    private async Task<AttemptContext?> HeldAsync(Guid attemptId, Guid runnerId, CancellationToken cancellationToken)
    {
        var context = await AttemptContext.LoadAsync(database, configuration, time, attemptId, track: true, cancellationToken).ConfigureAwait(false);
        return context?.Attempt is { Status: AttemptStatus.Running } attempt && attempt.RunnerId == runnerId ? context : null;
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<string, string>>> StepOutputsAsync(Guid attemptId, CancellationToken cancellationToken) =>
        Outputs(await database.JobSteps.AsNoTracking().Where(s => s.AttemptId == attemptId).ToListAsync(cancellationToken).ConfigureAwait(false));

    private static readonly string[] TooManyWarnings = ["The runner reported more warnings than can be kept."];

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Outputs(IEnumerable<JobStep> steps) =>
        steps.Where(s => s.StepId is not null && s.Outputs is not null)
            .ToDictionary(s => s.StepId!, s => RunOutputs.Read(s.Outputs), StringComparer.Ordinal);

    private static SecretRedactor Redactor(AttemptContext context) => new(context.Values.Secrets.Values);

    private static JobStepStatus ToStatus(CoreStepOutcome outcome) => outcome switch
    {
        CoreStepOutcome.Succeeded => JobStepStatus.Succeeded,
        CoreStepOutcome.Failed => JobStepStatus.Failed,
        CoreStepOutcome.Cancelled => JobStepStatus.Cancelled,
        CoreStepOutcome.TimedOut => JobStepStatus.TimedOut,
        CoreStepOutcome.Skipped => JobStepStatus.Skipped,
        _ => JobStepStatus.Errored,
    };

    private static CoreStepOutcome ToOutcome(JobStepStatus status) => status switch
    {
        JobStepStatus.Succeeded => CoreStepOutcome.Succeeded,
        JobStepStatus.Failed => CoreStepOutcome.Failed,
        JobStepStatus.Cancelled => CoreStepOutcome.Cancelled,
        JobStepStatus.TimedOut => CoreStepOutcome.TimedOut,
        JobStepStatus.Skipped => CoreStepOutcome.Skipped,
        _ => CoreStepOutcome.Errored,
    };

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}
