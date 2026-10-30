using System.Text.Json.Serialization;
using AutoCanvas.Server.Api;
using AutoCanvas.Server.Application;
using AutoCanvas.Server.Configuration;
using AutoCanvas.Server.Database;
using AutoCanvas.Server.Health;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Hosting;

/// <summary>Composition root of the server: services on the builder, pipeline on the app.</summary>
internal static class ServerBuilderExtensions
{
    public static WebApplicationBuilder AddAutoCanvasServer(this WebApplicationBuilder builder)
    {
        AddAutoCanvasEnvironment(builder.Configuration, AutoCanvasEnvironmentSource.ReadProcessEnvironment());

        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<ServerOptions>()
            .BindConfiguration(ServerOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddAutoCanvasProblemDetails();
        services.AddAutoCanvasOpenApi();
        services.AddHealthChecks();
        builder.AddAutoCanvasDatabase();
        services.AddAutoCanvasApplication();

        return builder;
    }

    public static WebApplication MapAutoCanvasServer(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapOpenApi();
        app.MapAutoCanvasHealth();
        app.MapAutoCanvasApi();

        return app;
    }

    /// <summary>
    /// Adds the <c>AUTOCANVAS_*</c> variables just before the command line, so that command-line
    /// arguments still win over them and they win over every settings file.
    /// </summary>
    internal static void AddAutoCanvasEnvironment(
        IConfigurationBuilder configuration,
        IReadOnlyDictionary<string, string?> environment)
    {
        var source = new AutoCanvasEnvironmentSource(environment);
        var commandLine = configuration.Sources.ToList().FindIndex(s => s is CommandLineConfigurationSource);
        if (commandLine < 0)
        {
            configuration.Add(source);
        }
        else
        {
            configuration.Sources.Insert(commandLine, source);
        }
    }
}
