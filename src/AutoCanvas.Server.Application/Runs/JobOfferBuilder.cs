using AutoCanvas.Core.Expressions;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Variables;

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
        var context = await AttemptContext.LoadAsync(database, configuration, time, attemptId, track: false, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return UseCase.Failure<OfferedJob>(UseCaseError.NotFound("The job attempt does not exist."));
        }

        var (attempt, job, run, step, values) = context;
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
            var env = RenderAll(service.Env, values, keepSecrets: true);
            if (service.Preset is { } name)
            {
                var preset = ServicePresets.Of(name);
                foreach (var (key, value) in preset.Env)
                {
                    env.TryAdd(key, value);
                }

                services.Add(new OfferedService(service.Name, $"{preset.Image}:{service.Version ?? preset.DefaultVersion}", env, service.Ready ?? preset.Ready, ServiceReadyTimeout));
            }
            else
            {
                services.Add(new OfferedService(service.Name, new SecretReads(values).Render(service.Image!), env, service.Ready, ServiceReadyTimeout));
            }
        }

        return UseCase.Success(new OfferedJob(
            attempt.Id,
            run.Id,
            attempt.Number,
            run.AutomationName,
            job.Key,
            job.Name,
            TimeSpan.FromMinutes(attempt.TimeoutMinutes),
            step.Image is null ? null : new SecretReads(values).Render(step.Image),
            checkouts,
            services,
            RenderAll(step.Env, values, keepSecrets: false),
            [.. step.Reports.Select(r => new OfferedReport(r.Format, r.Paths, r.Name ?? r.Format))]));
    }

    private static UseCaseResult<OfferedJob> Unsupported(string message) => UseCase.Failure<OfferedJob>(UseCaseError.Invalid("job", message));

    /// <summary>
    /// Renders every value; without <paramref name="keepSecrets"/>, entries that read a secret are
    /// left out — a job's environment is sent without secrets, the steps get those entries.
    /// </summary>
    internal static Dictionary<string, string> RenderAll(IReadOnlyDictionary<string, string> entries, IValueResolver values, bool keepSecrets)
    {
        var rendered = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, text) in entries)
        {
            var reads = new SecretReads(values);
            var value = reads.Render(text);
            if (keepSecrets || reads.Values.Count == 0)
            {
                rendered[name] = value;
            }
        }

        return rendered;
    }
}
