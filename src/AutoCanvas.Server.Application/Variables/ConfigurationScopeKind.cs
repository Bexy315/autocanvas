namespace AutoCanvas.Server.Application.Variables;

/// <summary>The kinds of places variables and secrets are defined, in the order they resolve.</summary>
public enum ConfigurationScopeKind
{
    /// <summary>The whole instance; set by admins.</summary>
    Instance,

    /// <summary>A folder and, by inheritance, everything below it.</summary>
    Folder,

    /// <summary>One automation: its secrets, and its variables in the file.</summary>
    Automation,
}
