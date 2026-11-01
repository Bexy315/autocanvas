using AutoCanvas.Runner.Connection;
using AutoCanvas.Runner.Docker;
using AutoCanvas.Runner.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Runner;

/// <summary>The runner's host: configuration from files, <c>AUTOCANVAS_*</c> variables and arguments, and its services.</summary>
internal static class RunnerHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

        // AUTOCANVAS_RUNNER__SERVER sets Runner:Server; added after the defaults so it wins over files.
        builder.Configuration.AddEnvironmentVariables("AUTOCANVAS_");
        builder.Configuration.AddCommandLine(args);
        builder.Services.AddAutoCanvasRunner();
        return builder;
    }

    public static IServiceCollection AddAutoCanvasRunner(this IServiceCollection services)
    {
        services.AddOptions<RunnerOptions>().BindConfiguration(RunnerOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<RunnerOptions>, RunnerOptionsValidator>();

        // Long enough for running jobs to finish while the runner drains.
        services.AddOptions<HostOptions>().Configure<IOptions<RunnerOptions>>((host, runner) => host.ShutdownTimeout = runner.Value.ShutdownTimeout + TimeSpan.FromMinutes(2));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider => ServerChannel.For(provider.GetRequiredService<IOptions<RunnerOptions>>().Value.Server!));
        services.TryAddSingleton<IContainerRuntime, DockerRuntime>();
        services.AddSingleton<Outbox>();
        services.AddSingleton<RunnerExitCode>();
        services.AddSingleton<JobRegistry>();
        services.AddSingleton(provider => new JobServices(
            provider.GetRequiredService<IContainerRuntime>(),
            provider.GetRequiredService<Outbox>(),
            provider.GetRequiredService<ServerChannel>(),
            provider.GetRequiredService<IOptions<RunnerOptions>>().Value,
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<JobFactory>();
        services.AddSingleton(provider => new LeftoverSweeper(
            provider.GetRequiredService<IContainerRuntime>(),
            provider.GetRequiredService<IOptions<RunnerOptions>>().Value,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LeftoverSweeper>>()));
        services.AddHostedService<RunnerConnection>();
        return services;
    }
}
