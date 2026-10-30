namespace AutoCanvas.Server.Configuration;

/// <summary>General server settings (configuration section <c>Server</c>).</summary>
internal sealed class ServerOptions
{
    public const string SectionName = "Server";

    /// <summary>
    /// The absolute URL users reach the server at, when it differs from what the server sees
    /// behind a reverse proxy. Used for links and cookies; optional.
    /// </summary>
    public string? PublicUrl { get; set; }
}
