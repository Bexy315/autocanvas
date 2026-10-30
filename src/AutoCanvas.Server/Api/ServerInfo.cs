using System.Reflection;

namespace AutoCanvas.Server.Api;

/// <summary>What <c>GET /api/v1/info</c> returns.</summary>
/// <param name="Product">Always <c>AutoCanvas</c>.</param>
/// <param name="Version">The server's version, including the source revision when built from git.</param>
internal sealed record ServerInfo(string Product, string Version)
{
    public static ServerInfo Current { get; } = new(
        "AutoCanvas",
        typeof(ServerInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
}
