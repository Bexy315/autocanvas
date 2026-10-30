using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace AutoCanvas.Server.Tests;

/// <summary>
/// Hosts the server in memory for API tests: <c>Testing</c> environment, a controllable clock,
/// and optional configuration and service overrides per test.
/// </summary>
public sealed class AutoCanvasFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> settings = new(StringComparer.Ordinal);
    private readonly List<Action<IServiceCollection>> serviceOverrides = [];
    private readonly List<Action<IApplicationBuilder>> appendedMiddleware = [];

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public AutoCanvasFactory With(string key, string? value)
    {
        settings[key] = value;
        return this;
    }

    public AutoCanvasFactory WithServices(Action<IServiceCollection> configure)
    {
        serviceOverrides.Add(configure);
        return this;
    }

    /// <summary>Middleware that runs after the server's own pipeline, inside its error handling.</summary>
    public AutoCanvasFactory WithMiddleware(Action<IApplicationBuilder> configure)
    {
        appendedMiddleware.Add(configure);
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            foreach (var configure in serviceOverrides)
            {
                configure(services);
            }

            if (appendedMiddleware.Count > 0)
            {
                services.AddSingleton<IStartupFilter>(new AppendMiddleware(appendedMiddleware));
            }
        });
    }

    private sealed class AppendMiddleware(IReadOnlyList<Action<IApplicationBuilder>> middleware) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            foreach (var configure in middleware)
            {
                configure(app);
            }
        };
    }
}
