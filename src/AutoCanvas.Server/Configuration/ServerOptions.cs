namespace AutoCanvas.Server.Configuration;

/// <summary>General server settings (configuration section <c>Server</c>).</summary>
internal sealed class ServerOptions
{
    public const string SectionName = "Server";

    /// <summary>Default of <see cref="DataDirectory"/>.</summary>
    public const string DefaultDataDirectory = "data";

    /// <summary>
    /// The absolute URL users reach the server at, when it differs from what the server sees
    /// behind a reverse proxy. Used for links and cookies; optional.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Where the server keeps its files, such as the default SQLite database. Relative paths are
    /// resolved against the server's content root.
    /// </summary>
    public string DataDirectory { get; set; } = DefaultDataDirectory;
}
