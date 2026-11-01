using AutoCanvas.Runner.Docker;
using Docker.DotNet;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Removes what an earlier run of this runner left behind — after a crash, say — before it takes
/// jobs: its containers (found by their labels) and the job directories. Other runners on the same
/// Docker host keep theirs.
/// </summary>
internal sealed partial class LeftoverSweeper(IContainerRuntime runtime, RunnerOptions options, ILogger<LeftoverSweeper> logger)
{
    public async Task SweepAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        try
        {
            var containers = await runtime.RemoveLabelledAsync(ContainerLabels.Runner, runnerId.ToString(), cancellationToken).ConfigureAwait(false);
            if (containers > 0)
            {
                LogRemovedContainers(logger, containers);
            }
        }
        catch (Exception exception) when (exception is DockerApiException or IOException or HttpRequestException)
        {
            LogDockerUnavailable(logger, exception);
        }

        if (!Directory.Exists(options.WorkRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(options.WorkRoot))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                LogRemovedDirectory(logger, directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Files a job's root user made; remove them by hand or with a container as root.
                LogDirectoryKept(logger, directory, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} container(s) an earlier run of the runner left behind")]
    private static partial void LogRemovedContainers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed the job directory {Directory} an earlier run of the runner left behind")]
    private static partial void LogRemovedDirectory(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the job directory {Directory} an earlier run of the runner left behind")]
    private static partial void LogDirectoryKept(ILogger logger, string directory, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not look for containers an earlier run of the runner left behind")]
    private static partial void LogDockerUnavailable(ILogger logger, Exception exception);
}
