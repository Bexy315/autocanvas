using System.Globalization;
using System.Text;
using AutoCanvas.Core.Secrets;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Docker;
using Docker.DotNet;
using Google.Protobuf.WellKnownTypes;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Runs a job's script steps in its container, one <c>exec</c> each (runner protocol,
/// <em>Executing steps</em>). The script and its output file live in <c>/autocanvas</c>; a stopped
/// step's shell gets SIGTERM, and if it has not ended after <see cref="JobServices.GracePeriod"/> the
/// container is restarted, which ends every process in it and keeps the workspace for the next step.
/// </summary>
internal sealed class ScriptStep(JobServices services, JobPaths paths, string containerId, LogBatcher log)
{
    // The shell's process id goes to a file, so that the step can be stopped from outside.
    private const string Wrapper = "echo $$ > \"/autocanvas/pid-$2\"; exec \"$0\" \"$1\"";

    private readonly HashSet<string> shells = new(StringComparer.Ordinal);

    /// <summary>Runs the step until it ends, is cancelled with <paramref name="cancel"/>, or runs out of time.</summary>
    /// <param name="step">The step.</param>
    /// <param name="cancel">Stops the step because the job was cancelled.</param>
    /// <param name="abort">Gives up on the container altogether, when the runner stops.</param>
    public async Task<StepCompleted> RunAsync(Step step, CancellationToken cancel, CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(step);
        var redactor = new SecretRedactor(step.MaskValues);
        var index = (int)step.Index;
        var script = step.Script;
        var directory = JobPaths.InContainer(script.WorkingDirectory);
        if (directory is null)
        {
            return Errored(step, redactor, $"working_directory '{script.WorkingDirectory}' is not a path inside the workspace.");
        }

        try
        {
            var shell = script.Shell;
            if (!await HasShellAsync(shell, abort).ConfigureAwait(false))
            {
                // A shell the server chose (the step named none) falls back to sh, as the format promises.
                if (!script.DefaultShell || string.Equals(shell, "sh", StringComparison.Ordinal) || !await HasShellAsync("sh", abort).ConfigureAwait(false))
                {
                    return Errored(step, redactor, $"The job's image has no {shell}, which the step runs in.");
                }

                log.Write(index, LogStream.System, $"The job's image has no {shell}; running the step with sh.", redactor);
                shell = "sh";
            }

            Directory.CreateDirectory(paths.OnRunner(script.WorkingDirectory)!);
            var number = index.ToString(CultureInfo.InvariantCulture);
            await File.WriteAllTextAsync(Path.Combine(paths.Meta, $"step-{number}"), script.Script.ReplaceLineEndings("\n"), new UTF8Encoding(false), abort).ConfigureAwait(false);
            var outputFile = Path.Combine(paths.Meta, $"output-{number}");
            await File.WriteAllTextAsync(outputFile, string.Empty, abort).ConfigureAwait(false);

            var env = new Dictionary<string, string>(step.Env, StringComparer.Ordinal) { ["AUTOCANVAS_OUTPUT"] = $"{JobPaths.ContainerMeta}/output-{number}" };
            var exec = new ExecSpec(["sh", "-c", Wrapper, shell, $"{JobPaths.ContainerMeta}/step-{number}", number], directory, env);
            var (outcome, exitCode) = await ExecuteAsync(step, exec, redactor, cancel, abort).ConfigureAwait(false);
            var completed = Completed(step, outcome, exitCode);
            foreach (var (name, value) in await StepOutputs.ReadAsync(outputFile, abort).ConfigureAwait(false))
            {
                completed.Outputs[name] = redactor.Redact(value);
            }

            return completed;
        }
        catch (Exception exception) when (exception is DockerApiException or IOException or HttpRequestException or UnauthorizedAccessException)
        {
            return Errored(step, redactor, $"The runner could not run the step: {exception.Message}");
        }
    }

    internal static StepCompleted Errored(Step step, SecretRedactor redactor, string error)
    {
        var completed = new StepCompleted { JobId = string.Empty, StepIndex = step.Index, Outcome = StepOutcome.Errored, Error = redactor.Redact(error) };
        return completed;
    }

    private static async Task<bool> EndsWithinAsync(Task task, TimeSpan wait) => await Task.WhenAny(task, Task.Delay(wait)).ConfigureAwait(false) == task;

    private StepCompleted Completed(Step step, StepOutcome outcome, long? exitCode)
    {
        var completed = new StepCompleted
        {
            StepIndex = step.Index,
            Outcome = outcome,
            FinishedAt = Timestamp.FromDateTimeOffset(services.Time.GetUtcNow()),
        };
        if (exitCode is { } code)
        {
            completed.ExitCode = (int)code;
        }

        return completed;
    }

    private async Task<(StepOutcome Outcome, long? ExitCode)> ExecuteAsync(Step step, ExecSpec exec, SecretRedactor redactor, CancellationToken cancel, CancellationToken abort)
    {
        using var timeout = new CancellationTokenSource();
        if (step.Timeout is { } limit)
        {
            timeout.CancelAfter(limit.ToTimeSpan());
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
        var index = (int)step.Index;
        var running = services.Runtime.ExecAsync(
            containerId,
            exec,
            line =>
            {
                log.Write(index, line.IsError ? LogStream.Stderr : LogStream.Stdout, line.Text, redactor);
                return Task.CompletedTask;
            },
            abort);
        await Task.WhenAny(running, Task.Delay(Timeout.Infinite, stop.Token)).ConfigureAwait(false);
        if (running.IsCompleted)
        {
            var exitCode = await running.ConfigureAwait(false);
            return (exitCode == 0 ? StepOutcome.Succeeded : StepOutcome.Failed, exitCode);
        }

        var timedOut = timeout.IsCancellationRequested && !cancel.IsCancellationRequested;
        log.Write(index, LogStream.System, timedOut ? "The step ran out of time; stopping it." : "The job was cancelled; stopping the step.", redactor);
        await StopAsync(exec.Command[^1], running, abort).ConfigureAwait(false);
        return (timedOut ? StepOutcome.TimedOut : StepOutcome.Cancelled, null);
    }

    private async Task StopAsync(string number, Task running, CancellationToken abort)
    {
        var kill = new ExecSpec(["sh", "-c", $"kill -TERM \"$(cat /autocanvas/pid-{number})\" 2>/dev/null; true"], JobPaths.ContainerWorkspace, new Dictionary<string, string>(StringComparer.Ordinal));
        await services.Runtime.ExecAsync(containerId, kill, _ => Task.CompletedTask, abort).ConfigureAwait(false);
        if (!await EndsWithinAsync(running, services.GracePeriod).ConfigureAwait(false))
        {
            await services.Runtime.RestartAsync(containerId, abort).ConfigureAwait(false);
            await EndsWithinAsync(running, services.GracePeriod).ConfigureAwait(false);
        }
    }

    private async Task<bool> HasShellAsync(string shell, CancellationToken abort)
    {
        if (!RunnerCapabilities.Shells.Contains(shell, StringComparer.Ordinal))
        {
            return false;
        }

        if (shells.Contains(shell))
        {
            return true;
        }

        var check = new ExecSpec(["sh", "-c", "command -v \"$0\" >/dev/null", shell], JobPaths.ContainerWorkspace, new Dictionary<string, string>(StringComparer.Ordinal));
        var found = await services.Runtime.ExecAsync(containerId, check, _ => Task.CompletedTask, abort).ConfigureAwait(false) == 0;
        if (found)
        {
            shells.Add(shell);
        }

        return found;
    }
}
