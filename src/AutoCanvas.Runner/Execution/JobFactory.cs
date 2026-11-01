using AutoCanvas.Protocol.Runner.V1;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.Runner.Execution;

/// <summary>Creates the execution of an accepted job with the runner's services.</summary>
internal sealed class JobFactory(JobServices services, ILogger<JobExecution> logger)
{
    public JobExecution Create(Job job, RunnerCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new JobExecution(job, services, credential.RunnerId, new ReportUploader(services.Server, credential), logger);
    }
}
