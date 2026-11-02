using AutoCanvas.Protocol.Runner.V1;
using AppLogLine = AutoCanvas.Server.Application.Runs.LogLine;
using AppReportFormat = AutoCanvas.Reports.ReportFormat;
using CoreStepOutcome = AutoCanvas.Core.Orchestration.StepOutcome;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Reports;
using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Application.Runs;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace AutoCanvas.Server.Runners;

/// <summary>Translates between the runner protocol's messages and the application's types.</summary>
internal static class ProtocolMapping
{
    /// <summary>The capabilities a runner announced; an error when they cannot be used.</summary>
    public static UseCaseResult<RunnerCapabilities> ToCapabilities(Capabilities? capabilities)
    {
        if (capabilities is null)
        {
            return UseCase.Failure<RunnerCapabilities>(UseCaseError.Invalid("capabilities", "capabilities must be set."));
        }

        RunnerExecutor? executor = capabilities.Executor switch
        {
            Executor.Container => RunnerExecutor.Container,
            Executor.Host => RunnerExecutor.Host,
            _ => null,
        };
        if (executor is null)
        {
            return UseCase.Failure<RunnerCapabilities>(UseCaseError.Invalid("capabilities.executor", "capabilities.executor must be EXECUTOR_CONTAINER or EXECUTOR_HOST."));
        }

        var mapped = new RunnerCapabilities(
            [.. capabilities.Labels],
            executor.Value,
            capabilities.Os,
            capabilities.Arch,
            [.. capabilities.NodeTypes],
            (int)Math.Min(capabilities.MaxConcurrentJobs, int.MaxValue),
            capabilities.RunnerVersion,
            [.. capabilities.Shells]);
        return mapped.Check() is { } invalid ? UseCase.Failure<RunnerCapabilities>(invalid) : UseCase.Success(mapped);
    }

    /// <summary>The offered job as sent in <see cref="JobOffer"/>.</summary>
    public static Job ToJob(OfferedJob offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        var job = new Job
        {
            JobId = offered.AttemptId.ToString(),
            RunId = offered.RunId.ToString(),
            Attempt = (uint)offered.Attempt,
            AutomationName = offered.AutomationName,
            JobKey = offered.JobKey,
            DisplayName = offered.DisplayName,
            Timeout = Duration.FromTimeSpan(offered.Timeout),
            Image = offered.Image ?? string.Empty,
        };
        foreach (var (name, value) in offered.Env)
        {
            job.Env[name] = value;
        }

        foreach (var checkout in offered.Checkouts)
        {
            job.Checkouts.Add(new Checkout
            {
                Url = checkout.Url.AbsoluteUri,
                Ref = checkout.Ref ?? string.Empty,
                Path = checkout.Path ?? string.Empty,
                Depth = (uint)Math.Max(0, checkout.Depth),
            });
        }

        foreach (var service in offered.Services)
        {
            var mapped = new Service
            {
                Name = service.Name,
                Image = service.Image,
                ReadyCommand = service.ReadyCommand ?? string.Empty,
                ReadyTimeout = Duration.FromTimeSpan(service.ReadyTimeout),
            };
            foreach (var (name, value) in service.Env)
            {
                mapped.Env[name] = value;
            }

            job.Services.Add(mapped);
        }

        foreach (var report in offered.Reports)
        {
            var spec = new ReportSpec { Format = ToFormat(report.Format), Name = report.Name };
            spec.Paths.Add(report.Paths);
            job.Reports.Add(spec);
        }

        return job;
    }

    /// <summary>A resolved step as sent in <see cref="RunStep"/>.</summary>
    public static Step ToStep(ResolvedStep resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        var step = new Step
        {
            Index = (uint)resolved.Index,
            Id = resolved.StepId ?? string.Empty,
            DisplayName = resolved.Name,
        };
        if (resolved.Timeout is { } timeout)
        {
            step.Timeout = Duration.FromTimeSpan(timeout);
        }

        foreach (var (name, value) in resolved.Env)
        {
            step.Env[name] = value;
        }

        step.MaskValues.Add(resolved.MaskValues);
        if (resolved.Script is { } script)
        {
            step.Script = new ScriptAction { Shell = script.Shell, Script = script.Text, WorkingDirectory = script.WorkingDirectory ?? string.Empty, DefaultShell = script.DefaultShell };
        }
        else if (resolved.Node is { } node)
        {
            step.Node = new NodeAction { Type = node.Type, Inputs = Google.Protobuf.JsonParser.Default.Parse<Struct>(node.Inputs) };
        }

        return step;
    }

    /// <summary>What a runner reported about a step, as the application reads it.</summary>
    public static StepReport ToReport(StepCompleted completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        var outcome = completed.Outcome switch
        {
            Protocol.Runner.V1.StepOutcome.Succeeded => CoreStepOutcome.Succeeded,
            Protocol.Runner.V1.StepOutcome.Failed => CoreStepOutcome.Failed,
            Protocol.Runner.V1.StepOutcome.Cancelled => CoreStepOutcome.Cancelled,
            Protocol.Runner.V1.StepOutcome.TimedOut => CoreStepOutcome.TimedOut,
            _ => CoreStepOutcome.Errored,
        };
        return new StepReport(
            (int)Math.Min(completed.StepIndex, int.MaxValue),
            outcome,
            completed.HasExitCode ? completed.ExitCode : null,
            completed.Outputs.ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal),
            string.IsNullOrEmpty(completed.Error) ? null : completed.Error,
            completed.FinishedAt?.ToDateTimeOffset());
    }

    /// <summary>A chunk of log lines, as the application reads it.</summary>
    public static ReceivedLogChunk ToChunk(LogChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return new ReceivedLogChunk(
            (long)Math.Min(chunk.Sequence, long.MaxValue),
            chunk.SourceCase == LogChunk.SourceOneofCase.StepIndex ? (int)Math.Min(chunk.StepIndex, int.MaxValue) : null,
            [.. chunk.Lines.Select(l => new AppLogLine(
                l.Time?.ToDateTimeOffset() ?? DateTimeOffset.UnixEpoch,
                l.Stream switch
                {
                    Protocol.Runner.V1.LogStream.Stderr => LogSource.Stderr,
                    Protocol.Runner.V1.LogStream.System => LogSource.System,
                    _ => LogSource.Stdout,
                },
                l.Text))]);
    }

    /// <summary>The header of a report upload; <see langword="null"/> without a job id or a format.</summary>
    public static ReportFile? ToReportFile(ReportHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        AppReportFormat? format = header.Format switch
        {
            ReportFormat.Junit => AppReportFormat.JUnit,
            ReportFormat.Trx => AppReportFormat.Trx,
            ReportFormat.Cobertura => AppReportFormat.Cobertura,
            _ => null,
        };
        return ToId(header.JobId) is { } attempt && format is { } known
            ? new ReportFile(attempt, known, header.Name, header.Path, (long)Math.Min(header.Size, long.MaxValue))
            : null;
    }

    /// <summary>Ids in messages are attempt or run UUIDs; anything else names nothing.</summary>
    public static Guid? ToId(string id) => Guid.TryParse(id, out var parsed) ? parsed : null;

    /// <summary>The gRPC status for a use case's error.</summary>
    public static RpcException ToRpcException(UseCaseError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = error.Kind switch
        {
            UseCaseErrorKind.Validation => StatusCode.InvalidArgument,
            UseCaseErrorKind.NotFound => StatusCode.NotFound,
            UseCaseErrorKind.Conflict => StatusCode.AlreadyExists,
            UseCaseErrorKind.Unauthorized => StatusCode.Unauthenticated,
            UseCaseErrorKind.Forbidden => StatusCode.PermissionDenied,
            UseCaseErrorKind.TooManyRequests => StatusCode.ResourceExhausted,
            _ => StatusCode.FailedPrecondition,
        };
        return new RpcException(new Status(code, error.Message));
    }

    private static ReportFormat ToFormat(string format) => format switch
    {
        "junit" => ReportFormat.Junit,
        "trx" => ReportFormat.Trx,
        "cobertura" => ReportFormat.Cobertura,
        _ => ReportFormat.Unspecified,
    };
}
