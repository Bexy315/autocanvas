namespace AutoCanvas.Server.Application.Instance;

/// <summary>Where a setting's value comes from.</summary>
public enum SettingSource
{
    /// <summary>Not set anywhere.</summary>
    None,

    /// <summary>Set in the web UI (or the setup) and kept in the database.</summary>
    Database,

    /// <summary>Set in the server's configuration; it wins and cannot be changed in the web UI.</summary>
    Configuration,
}
