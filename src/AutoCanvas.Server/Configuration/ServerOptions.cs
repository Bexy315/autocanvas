using System.Net;
using System.Net.Sockets;

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

    /// <summary>
    /// Reverse proxies in front of the server whose <c>X-Forwarded-For</c> and
    /// <c>X-Forwarded-Proto</c> headers are believed: addresses or networks (<c>10.0.0.0/8</c>),
    /// separated by commas. Unset, those headers are ignored.
    /// </summary>
    public string? TrustedProxies { get; set; }

    /// <summary>The trusted proxies as networks (a single address is a network of one).</summary>
    public IReadOnlyList<IPNetwork> TrustedNetworks() =>
        [.. (TrustedProxies ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Network)];

    /// <summary>Reads one entry; throws <see cref="FormatException"/> when it is neither an address nor a network.</summary>
    internal static IPNetwork Network(string entry)
    {
        if (entry.Contains('/', StringComparison.Ordinal))
        {
            return IPNetwork.Parse(entry);
        }

        var address = IPAddress.Parse(entry);
        return new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
    }
}
