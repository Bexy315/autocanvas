using AutoCanvas.Reports;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>What a runner says about a report file before it sends the content (<c>ReportHeader</c>).</summary>
/// <param name="AttemptId">The job attempt, the protocol's <c>job_id</c>.</param>
/// <param name="Format">The file's format.</param>
/// <param name="Name">The report's label from the job's report specification.</param>
/// <param name="Path">Path of the file relative to the workspace.</param>
/// <param name="Size">Size in bytes, as the runner reports it.</param>
public sealed record ReportFile(Guid AttemptId, ReportFormat Format, string Name, string Path, long Size);
