namespace AutoCanvas.Runner.Docker;

/// <summary>
/// What the runner needs from a container engine. <see cref="DockerRuntime"/> talks to Docker;
/// tests replace it to drive jobs without one.
/// </summary>
internal interface IContainerRuntime
{
    /// <summary>Pulls the image unless it is there already.</summary>
    /// <param name="image">The image reference, such as <c>alpine:3.22</c>.</param>
    /// <param name="progress">Receives a line per pull step worth showing in the job's log.</param>
    /// <param name="cancellationToken">Cancels the pull.</param>
    Task PullAsync(string image, Func<string, Task> progress, CancellationToken cancellationToken);

    /// <summary>Creates and starts a container; returns its id.</summary>
    Task<string> StartAsync(ContainerSpec spec, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a command in a running container and streams its output line by line until it exits.
    /// Cancelling only stops reading; the process is stopped by the caller (with a kill command or
    /// <see cref="RestartAsync"/>).
    /// </summary>
    /// <returns>The command's exit code.</returns>
    Task<long> ExecAsync(string containerId, ExecSpec exec, Func<OutputLine, Task> output, CancellationToken cancellationToken);

    /// <summary>Restarts a container, which ends every process running in it.</summary>
    Task RestartAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Removes a container with its anonymous volumes; one that is gone already is fine.</summary>
    Task RemoveAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Removes every container that carries the label with this value; returns how many.</summary>
    Task<int> RemoveLabelledAsync(string label, string value, CancellationToken cancellationToken);
}
