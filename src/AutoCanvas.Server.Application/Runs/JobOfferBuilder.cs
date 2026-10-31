using AutoCanvas.Core.Expressions;
using AutoCanvas.Core.Model;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Resolves a claimed attempt into the job offered to the runner (ADR-0008): expressions in the
/// job's environment and services are rendered on the server, and environment entries that read a
/// secret are held back for the steps.
/// </summary>
public sealed class JobOfferBuilder(AutoCanvasDbContext database, ConfigurationService configuration, TimeProvider time)
{
    /// <summary>How long a service gets to become ready.</summary>
    public static readonly TimeSpan ServiceReadyTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The job for an attempt; an error when the job uses something the server cannot provide yet,
    /// such as checking out the automation's own repository.
    /// </summary>
    public async Task<UseCaseResult<OfferedJob>> BuildAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await database.JobAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is null)
        {
            return UseCase.Failure<OfferedJob>(UseCaseError.NotFound("The job attempt does not exist."));
        }

        var job = await database.RunJobs.AsNoTracking().FirstAsync(j => j.Id == attempt.RunJobId, cancellationToken).ConfigureAwait(false);
        var run = await database.Runs.AsNoTracking().FirstAsync(r => r.Id == job.RunId, cancellationToken).ConfigureAwait(false);
        var jobs = await database.RunJobs.AsNoTracking().Where(j => j.RunId == run.Id).OrderBy(j => j.Position).ToListAsync(cancellationToken).ConfigureAwait(false);
        var automation = RunEngine.Load(run);
        var step = (StepJob)automation.Jobs.First(j => string.Equals(j.Id, job.Key, StringComparison.Ordinal));
        var values = await new RunEngine(database, configuration, time).ValuesAsync(run, automation, jobs, cancellationToken).ConfigureAwait(false);

        var checkouts = new List<OfferedCheckout>();
        foreach (var repository in step.Checkout.Repositories)
        {
            if (!Uri.TryCreate(repository.Repository, UriKind.Absolute, out var url) || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                return Unsupported($"Checking out '{repository.Repository}' needs a connected repository, which AutoCanvas does not support yet; use an https address.");
            }

            checkouts.Add(new OfferedCheckout(url, repository.Ref, repository.Path, repository.Depth ?? 1));
        }

        var services = new List<OfferedService>();
        foreach (var service in step.Services)
        {
            if (service.Image is not { } image)
            {
                return Unsupported($"Service '{service.Name}' uses a preset, which AutoCanvas does not support yet; give it an image.");
            }

            services.Add(new OfferedService(service.Name, Render(image, values), RenderAll(service.Env, values, keepSecrets: true), service.Ready, ServiceReadyTimeout));
        }

        return UseCase.Success(new OfferedJob(
            attempt.Id,
            run.Id,
            attempt.Number,
            run.AutomationName,
            job.Key,
            job.Name,
            TimeSpan.FromMinutes(attempt.TimeoutMinutes),
            step.Image is null ? null : Render(step.Image, values),
            checkouts,
            services,
            RenderAll(step.Env, values, keepSecrets: false),
            [.. step.Reports.Select(r => new OfferedReport(r.Format, r.Paths, r.Name ?? r.Format))]));
    }

    private static UseCaseResult<OfferedJob> Unsupported(string message) => UseCase.Failure<OfferedJob>(UseCaseError.Invalid("job", message));

    private static string Render(string text, IValueResolver values) => Template.Parse(text).Template.Render(values);

    /// <summary>
    /// Renders every value; without <paramref name="keepSecrets"/>, entries that read a secret are
    /// left out — a job's environment is sent without secrets.
    /// </summary>
    private static Dictionary<string, string> RenderAll(IReadOnlyDictionary<string, string> entries, IValueResolver values, bool keepSecrets)
    {
        var rendered = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, text) in entries)
        {
            var reader = new SecretReadTracker(values);
            var value = Render(text, reader);
            if (keepSecrets || !reader.ReadSecret)
            {
                rendered[name] = value;
            }
        }

        return rendered;
    }

    /// <summary>Passes reads on and notes whether one of them was a secret.</summary>
    private sealed class SecretReadTracker(IValueResolver inner) : IValueResolver
    {
        public bool ReadSecret { get; private set; }

        public bool TryResolve(IReadOnlyList<string> path, out string? value)
        {
            ArgumentNullException.ThrowIfNull(path);
            ReadSecret |= path is ["secrets", ..];
            return inner.TryResolve(path, out value);
        }
    }
}
