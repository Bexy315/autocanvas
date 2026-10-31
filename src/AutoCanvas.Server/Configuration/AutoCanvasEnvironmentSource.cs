using System.Collections;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Reads configuration from <c>AUTOCANVAS_*</c> environment variables: the .NET convention
/// <c>AUTOCANVAS_Section__Key</c> plus a few documented short names (<see cref="Aliases"/>).
/// </summary>
/// <remarks>
/// Aliases are applied first, so the long form wins when both are set. The alias table is the
/// single source for the operations documentation; keep them in sync.
/// </remarks>
internal sealed class AutoCanvasEnvironmentSource(
    IReadOnlyDictionary<string, string?> environment,
    IReadOnlyDictionary<string, string>? aliases = null) : IConfigurationSource
{
    public const string Prefix = "AUTOCANVAS_";

    /// <summary>Short environment variable names and the configuration keys they set.</summary>
    public static IReadOnlyDictionary<string, string> Aliases { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AUTOCANVAS_URLS"] = "urls",
        ["AUTOCANVAS_BOOTSTRAP_ADMIN_USERNAME"] = "Bootstrap:AdminUserName",
        ["AUTOCANVAS_BOOTSTRAP_ADMIN_PASSWORD"] = "Bootstrap:AdminPassword",
        ["AUTOCANVAS_MASTER_KEY"] = "Secrets:MasterKey",
        ["AUTOCANVAS_MASTER_KEY_FILE"] = "Secrets:MasterKeyFile",
    };

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(environment, aliases ?? Aliases);

    /// <summary>Snapshot of the current process's environment variables.</summary>
    public static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = (string?)entry.Value;
        }

        return variables;
    }

    private sealed class Provider(
        IReadOnlyDictionary<string, string?> environment,
        IReadOnlyDictionary<string, string> aliases) : ConfigurationProvider
    {
        public override void Load()
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (variable, key) in aliases)
            {
                if (environment.TryGetValue(variable, out var value))
                {
                    data[key] = value;
                }
            }

            foreach (var (variable, value) in environment)
            {
                if (variable.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                    && !aliases.ContainsKey(variable)
                    && variable.Length > Prefix.Length)
                {
                    data[variable[Prefix.Length..].Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal)] = value;
                }
            }

            Data = data;
        }
    }
}
