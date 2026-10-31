namespace AutoCanvas.Server.Application.Variables;

/// <summary>Where a variable or secret is defined: the instance, a folder or one automation.</summary>
/// <param name="Kind">Which kind of place.</param>
/// <param name="Id">The folder or automation; none for the instance.</param>
public sealed record ConfigurationScope(ConfigurationScopeKind Kind, Guid? Id)
{
    /// <summary>The whole instance.</summary>
    public static ConfigurationScope Instance { get; } = new(ConfigurationScopeKind.Instance, null);

    /// <summary>A folder.</summary>
    public static ConfigurationScope Folder(Guid id) => new(ConfigurationScopeKind.Folder, id);

    /// <summary>One automation (secrets only).</summary>
    public static ConfigurationScope Automation(Guid id) => new(ConfigurationScopeKind.Automation, id);
}
