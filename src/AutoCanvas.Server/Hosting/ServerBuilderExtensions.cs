using System.Text.Json.Serialization;
using AutoCanvas.Server.Api;
using AutoCanvas.Server.Application;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Authentication;
using AutoCanvas.Server.Configuration;
using AutoCanvas.Server.Database;
using AutoCanvas.Server.Health;
using AutoCanvas.Server.Identity;
using AutoCanvas.Server.Runners;
using AutoCanvas.Server.Setup;
using AutoCanvas.Server.Application.Instance;
using AutoCanvas.Server.Application.Setup;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Forge;
using AutoCanvas.Forge.GitHub;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Hosting;

/// <summary>Composition root of the server: services on the builder, pipeline on the app.</summary>
internal static class ServerBuilderExtensions
{
    /// <param name="builder">The host to add the server to.</param>
    /// <param name="runsServer">
    /// The server itself starts, not a command: it may then generate a master key for an
    /// installation that has none (ADR-0020).
    /// </param>
    public static WebApplicationBuilder AddAutoCanvasServer(this WebApplicationBuilder builder, bool runsServer = false)
    {
        AddAutoCanvasEnvironment(builder.Configuration, AutoCanvasEnvironmentSource.ReadProcessEnvironment());
        MasterKeyDefaults.Apply(builder, generate: runsServer);

        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<ServerOptions>()
            .BindConfiguration(ServerOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

            // A missing or null value for a non-nullable property is a bad request, not a null.
            options.SerializerOptions.RespectNullableAnnotations = true;
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
        });

        services.AddAutoCanvasProblemDetails();
        services.AddAutoCanvasOpenApi();
        services.AddHealthChecks();
        builder.AddAutoCanvasDatabase();
        services.AddAutoCanvasAuthentication();
        services.AddAutoCanvasApplication();

        services.AddOptions<SecretsOptions>()
            .BindConfiguration(SecretsOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecretsOptions>, SecretsOptionsValidator>();

        // Reading the options validates them, so a missing or broken key is reported, not null.
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<SecretsOptions>>().Value.Load(out _)!);
        services.AddHostedService<MasterKeyCheckService>();

        services.AddOptions<AuditOptions>()
            .BindConfiguration(AuditOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuditOptions>, AuditOptionsValidator>();
        services.AddHostedService<AuditRetentionService>();

        AddRuns(services);
        builder.AddAutoCanvasRunners();

        services.AddOptions<BootstrapOptions>()
            .BindConfiguration(BootstrapOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<BootstrapOptions>, BootstrapOptionsValidator>();
        services.AddHostedService<BootstrapAdminService>();
        AddSetup(services);
        AddRepositories(services);

        return builder;
    }

    /// <summary>Repositories in folders and the forges they live on (ADR-0021).</summary>
    private static void AddRepositories(IServiceCollection services)
    {
        services.AddOptions<RepositoryOptions>().BindConfiguration(RepositoryOptions.SectionName).ValidateOnStart();
        services.AddSingleton<RepositoryOptionsSetup>();
        services.AddSingleton<IPostConfigureOptions<RepositoryOptions>>(provider => provider.GetRequiredService<RepositoryOptionsSetup>());
        services.AddSingleton<IValidateOptions<RepositoryOptions>>(provider => provider.GetRequiredService<RepositoryOptionsSetup>());
        services.AddSingleton<IForge>(provider => new GenericGitForge(provider.GetRequiredService<IOptions<RepositoryOptions>>().Value.GitTimeout));
        services.AddSingleton<IForge>(provider => new GitHubForge(provider.GetRequiredService<IOptions<RepositoryOptions>>().Value.GitTimeout));
        services.AddHttpClient<GitHubClient>(http => http.Timeout = TimeSpan.FromSeconds(30));
        services.AddHostedService<RepositoryPollService>();
    }

    /// <summary>The setup of a fresh installation and the instance settings (ADR-0020).</summary>
    private static void AddSetup(IServiceCollection services)
    {
        services.AddOptions<SetupOptions>().BindConfiguration(SetupOptions.SectionName);
        services.AddSingleton<SetupState>();
        services.AddSingleton<IInstanceConfiguration, InstanceConfiguration>();
        services.AddSingleton<IRunnerTokenHandoff, RunnerTokenFileHandoff>();

        // After the bootstrap admin, which may have created the first account.
        services.AddHostedService<SetupModeService>();
    }

    /// <summary>Runs: their retention, the job queue and its sweeper, triggers and webhooks.</summary>
    private static void AddRuns(IServiceCollection services)
    {
        services.AddOptions<RunOptions>()
            .BindConfiguration(RunOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RunOptions>, RunOptionsValidator>();
        services.AddHostedService<RunRetentionService>();
        services.AddOptions<QueueOptions>()
            .BindConfiguration(QueueOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<QueueOptions>, QueueOptionsValidator>();
        services.AddHostedService<QueueSweepService>();
        services.AddHostedService<TriggerTickService>();
        services.AddOptions<WebhookOptions>().BindConfiguration(WebhookOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<WebhookOptions>, WebhookOptionsValidator>();
        services.AddHostedService<JobSignalListenerService>();
    }

    public static WebApplication MapAutoCanvasServer(this WebApplication app)
    {
        UseTrustedProxies(app);
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAutoCanvasWebUi();
        app.UseMiddleware<SetupGateMiddleware>();
        app.UseAutoCanvasAuthentication();

        app.MapOpenApi().AllowAnonymous();
        app.MapAutoCanvasHealth();
        app.MapAutoCanvasApi();
        app.MapAutoCanvasRunners();
        app.MapAutoCanvasWebUi();

        return app;
    }

    /// <summary>
    /// Believes the client address and scheme that trusted reverse proxies forward
    /// (<c>Server:TrustedProxies</c>), so rate limits, the audit log and the <c>Secure</c> cookie see
    /// the real client. Without trusted proxies nothing changes.
    /// </summary>
    private static void UseTrustedProxies(WebApplication app)
    {
        var networks = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.TrustedNetworks();
        if (networks.Count == 0)
        {
            return;
        }

        var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        forwarded.KnownProxies.Clear();
        forwarded.KnownIPNetworks.Clear();
        foreach (var network in networks)
        {
            forwarded.KnownIPNetworks.Add(network);
        }

        app.UseForwardedHeaders(forwarded);
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
