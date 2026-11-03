using AutoCanvas.Server.Application.Instance;
using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Configuration;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Setup;

/// <summary>The instance settings the server's configuration sets, which win over the stored ones.</summary>
internal sealed class InstanceConfiguration(IOptions<ServerOptions> server, IOptions<RunnerOptions> runners) : IInstanceConfiguration
{
    public Uri? PublicUrl => server.Value.PublicUrl is { Length: > 0 } url ? new Uri(url, UriKind.Absolute) : null;

    public Uri? RunnerUrl => runners.Value.PublicUrl;
}
