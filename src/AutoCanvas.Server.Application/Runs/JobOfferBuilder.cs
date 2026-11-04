using AutoCanvas.Forge;
using AutoCanvas.Core.Expressions;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Core.Model;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Resolves a claimed attempt into the job offered to the runner (ADR-0008): expressions in the
/// job's environment and services are rendered on the server, and environment entries that read a
/// secret are held back for the steps.
/// </summary>
public sealed class JobOfferBuilder(AutoCanvasDbContext database, ConfigurationService configuration, RepositoryCredentials credentials, TimeProvider time)
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
        UseCaseResult<List<OfferedCheckout>> checkouts;
        try
        {
            checkouts = await CheckoutsAsync(run, step.Checkout, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            // Such as a GitHub repository whose installation token cannot be had now.
            checkouts = Unsupported<List<OfferedCheckout>>($"A repository cannot be checked out: {exception.Message}");
        }

        if (checkouts.Error is { } unsupported)
        {
            return UseCase.Failure<OfferedJob>(unsupported);
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
            checkouts.Value!,
            services,
            RenderAll(step.Env, values, keepSecrets: false),
            [.. step.Reports.Select(r => new OfferedReport(r.Format, r.Paths, r.Name ?? r.Format))]));
    }

    /// <summary>
    /// What the job checks out (ADR-0021): a repository automation its own commit unless it says
    /// otherwise; <c>self</c>; a repository connected in the run's folder or above, by name, with its
    /// credential; or a public https address.
    /// </summary>
    private async Task<UseCaseResult<List<OfferedCheckout>>> CheckoutsAsync(Run run, CheckoutSetting setting, CancellationToken cancellationToken)
    {
        var repositories = new RepositoryCheckouts(database, credentials);
        var self = await repositories.OfAutomationAsync(run.AutomationId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CheckoutRepository> wanted = setting.Kind switch
        {
            CheckoutKind.Default => self is null ? [] : [new CheckoutRepository(Self, null, null, null)],
            CheckoutKind.Disabled => [],
            _ => setting.Repositories,
        };

        var checkouts = new List<OfferedCheckout>();
        foreach (var repository in wanted)
        {
            var depth = repository.Depth ?? 1;
            if (string.Equals(repository.Repository, Self, StringComparison.Ordinal))
            {
                if (self is null)
                {
                    return Unsupported<List<OfferedCheckout>>("'checkout: self' needs an automation from a repository; this one is managed in AutoCanvas. Name a repository or an https address instead.");
                }

                checkouts.Add(await repositories.CheckoutAsync(self, repository.Ref ?? run.CommitSha ?? self.ResolvedBranch, repository.Path, depth, cancellationToken).ConfigureAwait(false));
            }
            else if (Uri.TryCreate(repository.Repository, UriKind.Absolute, out var url))
            {
                if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
                {
                    return Unsupported<List<OfferedCheckout>>($"Checking out '{repository.Repository}' needs an https address, or the name of a connected repository.");
                }

                checkouts.Add(new OfferedCheckout(url, repository.Ref, repository.Path, depth));
            }
            else if (run.SecretsWithheld)
            {
                return Unsupported<List<OfferedCheckout>>($"'{repository.Repository}' is not checked out: the run is of a pull request from a fork, which gets no secrets and no other repositories.");
            }
            else if (await repositories.ByNameAsync(run.FolderId, repository.Repository, cancellationToken).ConfigureAwait(false) is { } named)
            {
                checkouts.Add(await repositories.CheckoutAsync(named, repository.Ref ?? named.ResolvedBranch, repository.Path, depth, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                return Unsupported<List<OfferedCheckout>>($"No repository named '{repository.Repository}' is connected in the run's folder or the folders above it.");
            }
        }

        return UseCase.Success(checkouts);
    }

    private const string Self = "self";

    private static UseCaseResult<T> Unsupported<T>(string message) => UseCase.Failure<T>(UseCaseError.Invalid("job", message));

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
