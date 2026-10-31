using AutoCanvas.Core;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Variables;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>
/// Resolves variables and secrets along the chain of ADR-0013: explains to people where each name
/// comes from (never with secret values), and gives runs the values.
/// </summary>
public sealed class ConfigurationService(AutoCanvasDbContext database, SecretCipher cipher)
{
    private readonly FolderAccess folders = new(database);

    /// <summary>What everything in a folder can read (Viewer); variable values for Operators.</summary>
    public async Task<UseCaseResult<ConfigurationView>> ExplainFolderAsync(Actor actor, Guid folderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var folder = await folders.RequireAsync(actor, folderId, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<ConfigurationView>(denied);
        }

        var definitions = await DefinitionsAsync(folder.Value!.Chain, automationId: null, fileVariables: null, cancellationToken).ConfigureAwait(false);
        return UseCase.Success(Explain(definitions, folder.Value.Role >= FolderRole.Operator));
    }

    /// <summary>What an automation can read (Viewer on its folder); variable values for Operators.</summary>
    public async Task<UseCaseResult<ConfigurationView>> ExplainAutomationAsync(Actor actor, Guid automationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var automation = await LoadAsync(automationId, cancellationToken).ConfigureAwait(false);
        if (automation is null)
        {
            return UseCase.Failure<ConfigurationView>(ScopeAccess.AutomationNotFound);
        }

        var folder = await folders.RequireAsync(actor, automation.Value.FolderId, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        if (folder.Error is { } denied)
        {
            return UseCase.Failure<ConfigurationView>(denied.Kind == UseCaseErrorKind.NotFound ? ScopeAccess.AutomationNotFound : denied);
        }

        var definitions = await DefinitionsAsync(folder.Value!.Chain, automationId, FileVariables(automation.Value.Yaml), cancellationToken).ConfigureAwait(false);
        return UseCase.Success(Explain(definitions, folder.Value.Role >= FolderRole.Operator));
    }

    /// <summary>
    /// The values a run of the automation reads, secrets decrypted. For the run engine only: it
    /// checks no permissions, and its result must never be returned through the API.
    /// </summary>
    public async Task<ResolvedConfiguration?> ResolveAsync(Guid automationId, CancellationToken cancellationToken)
    {
        var automation = await LoadAsync(automationId, cancellationToken).ConfigureAwait(false);
        return automation is null
            ? null
            : await ResolveForRunAsync(automation.Value.FolderId, automationId, FileVariables(automation.Value.Yaml), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The values a run reads, for the file the run carries (its copy, not the automation's current
    /// version). Without <paramref name="automationId"/> (the automation was deleted) there are no
    /// automation secrets. For the run engine only, like <see cref="ResolveAsync"/>.
    /// </summary>
    public async Task<ResolvedConfiguration> ResolveForRunAsync(Guid folderId, Guid? automationId, IReadOnlyDictionary<string, string> fileVariables, CancellationToken cancellationToken)
    {
        var path = await database.Folders.AsNoTracking().Where(f => f.Id == folderId).Select(f => f.Path).FirstAsync(cancellationToken).ConfigureAwait(false);
        var chain = await folders.ChainAsync(path, cancellationToken).ConfigureAwait(false);
        var definitions = await DefinitionsAsync(chain, automationId, fileVariables, cancellationToken).ConfigureAwait(false);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _, value) in definitions.Variables)
        {
            variables[name] = value;
        }

        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var secret in definitions.Secrets)
        {
            // The key check at startup makes this a broken database rather than a wrong key.
            secrets[secret.Name] = cipher.Decrypt(secret.Scope, secret.NormalizedName, secret.Ciphertext)
                ?? throw new InvalidOperationException($"The secret '{secret.Name}' in {secret.Scope} cannot be decrypted with master key {cipher.Key.Id}.");
        }

        return new ResolvedConfiguration(variables, secrets);
    }

    private static IReadOnlyDictionary<string, string> FileVariables(string yaml) =>
        AutomationLoader.Load(yaml).Definition?.Variables ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static ConfigurationView Explain(Definitions definitions, bool showValues) => new(
        ConfigurationChain.Merge(definitions.Variables.Select(v => (v.Name, v.Source with { Value = showValues ? v.Value : null }))),
        ConfigurationChain.Merge(definitions.Secrets.Select(s => (s.Name, definitions.SourceOf(s.Scope)))));

    private async Task<(Guid FolderId, string Yaml)?> LoadAsync(Guid automationId, CancellationToken cancellationToken)
    {
        var found = await database.Automations.AsNoTracking()
            .Where(a => a.Id == automationId)
            .Select(a => new
            {
                a.FolderId,
                Yaml = database.AutomationVersions.Where(v => v.AutomationId == a.Id && v.Number == a.CurrentVersion).Select(v => v.Yaml).First(),
            })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return found is null ? null : (found.FolderId, found.Yaml);
    }

    /// <summary>Every definition that applies, in resolution order.</summary>
    private async Task<Definitions> DefinitionsAsync(
        IReadOnlyList<FolderChainEntry> chain, Guid? automationId, IReadOnlyDictionary<string, string>? fileVariables, CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, ConfigurationSource>(StringComparer.Ordinal)
        {
            [ValueScopes.Instance] = new(ConfigurationScopeKind.Instance, null, null, null),
        };
        foreach (var folder in ConfigurationChain.ApplyingFolders(chain))
        {
            sources[ValueScopes.Folder(folder.Id)] = new(ConfigurationScopeKind.Folder, folder.Id, folder.Name, null);
        }

        if (automationId is { } id)
        {
            sources[ValueScopes.Automation(id)] = new(ConfigurationScopeKind.Automation, null, null, null);
        }

        var order = sources.Keys.Select((key, index) => (key, index)).ToDictionary(p => p.key, p => p.index, StringComparer.Ordinal);
        var keys = sources.Keys.ToList();
        var stored = await database.Variables.AsNoTracking().Where(v => keys.Contains(v.Scope)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var secrets = await database.Secrets.AsNoTracking().Where(s => keys.Contains(s.Scope)).ToListAsync(cancellationToken).ConfigureAwait(false);

        var variables = stored
            .OrderBy(v => order[v.Scope])
            .Select(v => (v.Name, Source: sources[v.Scope], v.Value))
            .Concat((fileVariables ?? new Dictionary<string, string>(StringComparer.Ordinal))
                .Select(f => (Name: f.Key, Source: new ConfigurationSource(ConfigurationScopeKind.Automation, null, null, null), f.Value)))
            .ToList();
        return new Definitions(variables, [.. secrets.OrderBy(s => order[s.Scope])], sources);
    }

    private sealed record Definitions(
        IReadOnlyList<(string Name, ConfigurationSource Source, string Value)> Variables,
        IReadOnlyList<Secret> Secrets,
        IReadOnlyDictionary<string, ConfigurationSource> Sources)
    {
        public ConfigurationSource SourceOf(string scope) => Sources[scope];
    }
}
