using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Instance;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Instance;

/// <summary>
/// The installation's settings changed in the web UI (ADR-0020): kept in the database, unless the
/// server's configuration sets them, which wins. Changing them is for admins (checked by the host).
/// </summary>
public sealed class InstanceSettingsService(AutoCanvasDbContext database, IInstanceConfiguration configuration, IAuditLog audit, TimeProvider time)
{
    /// <summary>The settings in effect and where each comes from.</summary>
    public async Task<InstanceSettingsView> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await database.InstanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == InstanceSettings.SingletonId, cancellationToken).ConfigureAwait(false);
        return new InstanceSettingsView(Effective(configuration.PublicUrl, stored?.PublicUrl), Effective(configuration.RunnerUrl, stored?.RunnerUrl));
    }

    /// <summary>
    /// Stores both addresses; <see langword="null"/> or empty clears one. A setting the configuration
    /// sets cannot be changed here: it must be sent empty or with the configured value.
    /// </summary>
    public async Task<UseCaseResult<InstanceSettingsView>> UpdateAsync(Guid userId, string? publicAddress, string? runnerAddress, CancellationToken cancellationToken)
    {
        var (publicValue, publicProblem) = Parse("publicAddress", publicAddress, configuration.PublicUrl, "Server:PublicUrl");
        var (runnerValue, runnerProblem) = Parse("runnerAddress", runnerAddress, configuration.RunnerUrl, "Runners:PublicUrl");
        if ((publicProblem ?? runnerProblem) is { } problem)
        {
            return UseCase.Failure<InstanceSettingsView>(problem);
        }

        var settings = await database.InstanceSettings.FirstOrDefaultAsync(s => s.Id == InstanceSettings.SingletonId, cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            settings = new InstanceSettings { Id = InstanceSettings.SingletonId };
            database.InstanceSettings.Add(settings);
        }

        var changed = new List<string>();
        if (configuration.PublicUrl is null && settings.PublicUrl != publicValue)
        {
            settings.PublicUrl = publicValue;
            changed.Add("publicAddress");
        }

        if (configuration.RunnerUrl is null && settings.RunnerUrl != runnerValue)
        {
            settings.RunnerUrl = runnerValue;
            changed.Add("runnerAddress");
        }

        if (changed.Count > 0)
        {
            settings.UpdatedAt = time.GetUtcNow();
            settings.UpdatedBy = userId;
            audit.Record(new AuditEntry(AuditActions.InstanceUpdated)
            {
                TargetType = "instance",
                Details = AuditEntry.DetailsOf(
                    ("changed", changed),
                    ("publicAddress", settings.PublicUrl?.ToString()),
                    ("runnerAddress", settings.RunnerUrl?.ToString())),
            });
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return UseCase.Success(await GetAsync(cancellationToken).ConfigureAwait(false));
    }

    private static SettingValue Effective(Uri? configured, Uri? stored) =>
        configured is not null ? new SettingValue(configured, SettingSource.Configuration)
        : stored is not null ? new SettingValue(stored, SettingSource.Database)
        : new SettingValue(null, SettingSource.None);

    private static (Uri? Value, UseCaseError? Problem) Parse(string field, string? text, Uri? configured, string setting)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var url)
            || (!string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            || !string.Equals(url.AbsolutePath, "/", StringComparison.Ordinal) || url.Query.Length > 0 || url.Fragment.Length > 0 || url.UserInfo.Length > 0
            || url.OriginalString.Length > InstanceSettings.MaxUrlLength)
        {
            return (null, UseCaseError.Invalid(field, "Give an absolute http or https address without a path, such as https://autocanvas.example.org."));
        }

        if (configured is not null && configured != url)
        {
            return (null, UseCaseError.Conflict($"This address is set by the server's configuration ({setting}); change it there.", field));
        }

        return (url, null);
    }
}
