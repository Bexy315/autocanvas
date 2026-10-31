using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Server.Application.Common;
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
