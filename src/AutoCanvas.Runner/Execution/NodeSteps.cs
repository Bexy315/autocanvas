using AutoCanvas.Core.Nodes;
using AutoCanvas.Core.Secrets;
using AutoCanvas.Protocol.Runner.V1;
using Docker.DotNet;
using Google.Protobuf.WellKnownTypes;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Runs a job's node steps (ADR-0016): <c>http_request</c> from the runner itself,
/// <c>docker_build</c> in a BuildKit container next to the job's. A node that is stopped or runs out
/// of time ends as cancelled or timed out, like a script.
/// </summary>
internal sealed class NodeSteps(JobServices services, JobPaths paths, Guid runnerId, string jobId, LogBatcher log)
{
    public async Task<StepCompleted> RunAsync(Step step, CancellationToken cancel, CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(step);
        var redactor = new SecretRedactor(step.MaskValues);
        if (step.Node.Type is not (BuiltInNodeTypes.HttpRequest or BuiltInNodeTypes.DockerBuild))
        {
            return ScriptStep.Errored(step, redactor, $"This runner cannot run node type '{step.Node.Type}'.");
        }

        using var timeout = new CancellationTokenSource();
        if (step.Timeout is { } limit)
        {
            timeout.CancelAfter(limit.ToTimeSpan());
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token, abort);
        var context = new NodeContext(step, NodeInputs.From(step.Node.Inputs), log, redactor, stop.Token);
        NodeResult result;
        try
        {
            result = string.Equals(step.Node.Type, BuiltInNodeTypes.HttpRequest, StringComparison.Ordinal)
                ? await new HttpRequestNode(services.Http).RunAsync(context).ConfigureAwait(false)
                : await new DockerBuildNode(services, paths, runnerId, jobId).RunAsync(context).ConfigureAwait(false);
        }
        catch (NodeInputException exception)
        {
            result = NodeResult.Failed(exception.Message);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested && !abort.IsCancellationRequested)
        {
            var timedOut = timeout.IsCancellationRequested && !cancel.IsCancellationRequested;
            context.Write(LogStream.System, timedOut ? "The step ran out of time; it was stopped." : "The job was cancelled; the step was stopped.");
            return Ended(step, timedOut ? StepOutcome.TimedOut : StepOutcome.Cancelled, null, redactor);
        }
        catch (Exception exception) when (exception is DockerApiException or IOException)
        {
            return ScriptStep.Errored(step, redactor, $"The runner could not run the step: {exception.Message}");
        }

        var completed = Ended(step, result.Succeeded ? StepOutcome.Succeeded : StepOutcome.Failed, result.Error, redactor);
        foreach (var (name, value) in result.Outputs)
        {
            completed.Outputs[name] = redactor.Redact(value);
        }

        return completed;
    }

    private StepCompleted Ended(Step step, StepOutcome outcome, string? error, SecretRedactor redactor) => new()
    {
        StepIndex = step.Index,
        Outcome = outcome,
        Error = redactor.Redact(error),
        FinishedAt = Timestamp.FromDateTimeOffset(services.Time.GetUtcNow()),
    };
}
