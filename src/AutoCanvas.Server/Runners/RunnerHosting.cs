using AutoCanvas.Server.Application.Reports;
using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Configuration;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// The runner protocol's gRPC services and the endpoint they are served on (ADR-0017): a port of
/// their own that speaks only HTTP/2, so that runners can connect without TLS on a local network,
/// next to the HTTP API's port, which browsers reach with HTTP/1.1.
/// </summary>
internal static class RunnerHosting
{
    /// <summary>Configuration key of the runner endpoint's addresses.</summary>
    public const string UrlsKey = "Runners:Urls";

    /// <summary>Where runners connect when nothing is configured.</summary>
    public const string DefaultUrls = "http://localhost:5081";

    /// <summary>Where the HTTP API listens when nothing is configured, as in ASP.NET Core.</summary>
    public const string DefaultApiUrls = "http://localhost:5000";

    public static WebApplicationBuilder AddAutoCanvasRunners(this WebApplicationBuilder builder)
    {
        AddKestrelEndpoints(builder.Configuration);

        var services = builder.Services;
        services.AddOptions<RunnerOptions>()
            .BindConfiguration(RunnerOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RunnerOptions>, RunnerOptionsValidator>();
        // Runners send the logs; the limit applies to what they send.
        services.AddOptions<LogOptions>()
            .BindConfiguration(LogOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LogOptions>, LogOptionsValidator>();
        services.AddOptions<ReportOptions>()
            .BindConfiguration(ReportOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ReportOptions>, ReportOptionsValidator>();
        services.AddSingleton<RunnerSessions>();
        services.AddHostedService(provider => provider.GetRequiredService<RunnerSessions>());
        services.AddSingleton<RunnerAuthInterceptor>();
        services.AddGrpc(grpc => grpc.EnableDetailedErrors = false)
            .AddServiceOptions<RunnerGrpcService>(service => service.Interceptors.Add<RunnerAuthInterceptor>());
        return builder;
    }

    public static WebApplication MapAutoCanvasRunners(this WebApplication app)
    {
        app.MapGrpcService<RegistrationGrpcService>().AllowAnonymous();
        app.MapGrpcService<RunnerGrpcService>().AllowAnonymous();
        return app;
    }

    /// <summary>
    /// Turns <c>urls</c> and <see cref="UrlsKey"/> into Kestrel endpoints: the API's with the default
    /// protocols, the runners' with HTTP/2 only. Kestrel cannot serve HTTP/2 without TLS on an endpoint
    /// that also speaks HTTP/1.1. Whoever configures <c>Kestrel:Endpoints</c> takes over both.
    /// </summary>
    internal static void AddKestrelEndpoints(IConfigurationManager configuration)
    {
        if (configuration.GetSection("Kestrel:Endpoints").Exists())
        {
            return;
        }

        var endpoints = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var api = Split(configuration["urls"] ?? DefaultApiUrls);
        for (var i = 0; i < api.Length; i++)
        {
            endpoints[$"Kestrel:Endpoints:Api{i}:Url"] = api[i];
        }

        var runners = Split(configuration[UrlsKey] ?? DefaultUrls);
        for (var i = 0; i < runners.Length; i++)
        {
            endpoints[$"Kestrel:Endpoints:Runners{i}:Url"] = runners[i];
            endpoints[$"Kestrel:Endpoints:Runners{i}:Protocols"] = "Http2";
        }

        // The endpoints replace urls; left set, Kestrel would warn on every start that it ignores it.
        endpoints["urls"] = null;
        configuration.AddInMemoryCollection(endpoints);
    }

    private static string[] Split(string urls) => urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
