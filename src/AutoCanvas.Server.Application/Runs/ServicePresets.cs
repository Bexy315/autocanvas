using AutoCanvas.Core.Model;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// What a service <c>preset</c> stands for: image, environment and readiness check, defined once on
/// the server so that every runner starts the same container. A service's own <c>env</c> overrides
/// the preset's.
/// </summary>
public static class ServicePresets
{
    private static readonly Dictionary<ServicePreset, ServicePresetDefinition> Known = new()
    {
        [ServicePreset.Postgres] = new("postgres", "17", Env(("POSTGRES_PASSWORD", "postgres")), "pg_isready -U postgres"),
        [ServicePreset.MySql] = new("mysql", "8.4", Env(("MYSQL_ROOT_PASSWORD", "mysql")), "mysqladmin ping -h 127.0.0.1 -uroot -pmysql --silent"),
        [ServicePreset.Redis] = new("redis", "7", Env(), "redis-cli ping"),
    };

    /// <summary>The definition of <paramref name="preset"/>.</summary>
    public static ServicePresetDefinition Of(ServicePreset preset) => Known[preset];

    private static Dictionary<string, string> Env(params (string Name, string Value)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);
}
