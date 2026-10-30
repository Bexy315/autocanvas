using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Sqlite;
using AutoCanvas.Server.Configuration;
using AutoCanvas.Server.Health;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Database;

/// <summary>Chooses and registers the configured database provider.</summary>
internal static class DatabaseRegistration
{
    public const string DefaultSqliteFileName = "autocanvas.db";

    public static WebApplicationBuilder AddAutoCanvasDatabase(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        // The provider decides which services exist, so it is read before the container is built.
        // Invalid values still surface through the validator above when the host starts.
        var options = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (options.Provider == DatabaseProvider.Postgres)
        {
            services.AddAutoCanvasPostgres(options.ConnectionString ?? string.Empty);
        }
        else
        {
            var connectionString = options.ConnectionString ?? DefaultSqliteConnectionString(builder);
            services.AddAutoCanvasSqlite(connectionString);
        }

        services.AddScoped<DatabaseMigrator>();
        services.AddHostedService<DatabaseMigrationService>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [HealthEndpoints.ReadyTag]);
        return builder;
    }

    /// <summary>
    /// <c>autocanvas.db</c> in the data directory, which is created if missing, so a fresh
    /// installation needs no database settings at all (ADR-0010).
    /// </summary>
    private static string DefaultSqliteConnectionString(WebApplicationBuilder builder)
    {
        var dataDirectory = builder.Configuration[$"{ServerOptions.SectionName}:{nameof(ServerOptions.DataDirectory)}"]
            ?? ServerOptions.DefaultDataDirectory;
        var directory = Path.GetFullPath(dataDirectory, builder.Environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        return $"Data Source={Path.Combine(directory, DefaultSqliteFileName)}";
    }
}
