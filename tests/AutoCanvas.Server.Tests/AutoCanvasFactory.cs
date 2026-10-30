using AutoCanvas.Server.Application.Identity;
using AutoCanvas.TestSupport;
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
/// a fresh SQLite database (or the one given to <see cref="WithDatabase"/>), and optional
/// configuration and service overrides per test.
/// </summary>
public sealed class AutoCanvasFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> settings = new(StringComparer.Ordinal);
    private readonly List<Action<IServiceCollection>> serviceOverrides = [];
    private readonly List<Action<IApplicationBuilder>> appendedMiddleware = [];
    private readonly SqliteTestDatabase ownDatabase = SqliteTestDatabase.Create();

    public AutoCanvasFactory() => UseDatabaseSettings(ownDatabase);

    /// <summary>
    /// Starts at the real time: the test client's cookie jar checks cookie expiry against the real
    /// clock, so a session cookie issued "in the past" would be dropped.
    /// </summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    public AutoCanvasFactory With(string key, string? value)
    {
        settings[key] = value;
        return this;
    }

    /// <summary>Runs the server on the given database instead of its own SQLite file.</summary>
    public AutoCanvasFactory WithDatabase(TestDatabase database)
    {
        UseDatabaseSettings(database);
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
        // UseSetting makes the values visible while Program configures services, which the
        // database registration needs; ConfigureAppConfiguration alone applies them too late.
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);

            // Real cost is 600,000 iterations; tests sign in often.
            services.AddSingleton(new PasswordHashingOptions { Iterations = 1_000 });
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

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await ownDatabase.DisposeAsync();
    }

    private void UseDatabaseSettings(TestDatabase database)
    {
        foreach (var (key, value) in database.Settings)
        {
            settings[key] = value;
        }
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
