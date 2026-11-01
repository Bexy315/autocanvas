using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Connection;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.FileSystemGlobbing;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Uploads a finished job's reports (runner protocol, <em>Reports</em>): every file in the
/// workspace that matches a report's paths, as a header and then chunks of 1 MiB. What cannot be
/// uploaded becomes a warning of the job, never a failure.
/// </summary>
internal sealed class ReportUploader(ServerChannel server, RunnerCredential credential)
{
    public const int ChunkSize = 1024 * 1024;

    public const int MaxAttempts = 3;

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <returns>A warning for each report that found no file, and for each file that could not be uploaded.</returns>
    public async Task<List<string>> UploadAsync(Job job, string workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var warnings = new List<string>();
        foreach (var report in job.Reports)
        {
            var files = Find(report, workspace);
            if (files.Count == 0)
            {
                warnings.Add($"Report '{report.Name}': no file matches {string.Join(", ", report.Paths)}.");
                continue;
            }

            foreach (var file in files)
            {
                if (await UploadFileAsync(job.JobId, report, workspace, file, cancellationToken).ConfigureAwait(false) is { } problem)
                {
                    warnings.Add($"Report '{report.Name}' ({file}): {problem}");
                }
            }
        }

        return warnings;
    }

    /// <summary>The matching files, relative to the workspace with forward slashes, in order.</summary>
    public static List<string> Find(ReportSpec report, string workspace)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!Directory.Exists(workspace))
        {
            return [];
        }

        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddIncludePatterns(report.Paths);
        return [.. matcher.GetResultsInFullPath(workspace)
            .Select(path => Path.GetRelativePath(workspace, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    /// <returns>Why the file was not uploaded, or <see langword="null"/>.</returns>
    private async Task<string?> UploadFileAsync(string jobId, ReportSpec report, string workspace, string file, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await SendAsync(jobId, report, Path.Combine(workspace, file), file, cancellationToken).ConfigureAwait(false);
                return null;
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Unavailable && attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException exception)
            {
                return exception.Status.Detail;
            }
            catch (IOException exception)
            {
                return exception.Message;
            }
        }
    }

    private async Task SendAsync(string jobId, ReportSpec report, string path, string relative, CancellationToken cancellationToken)
    {
        var content = File.OpenRead(path);
        await using (content.ConfigureAwait(false))
        {
            using var call = server.Runner.UploadReport(ServerChannel.Authorization(credential), cancellationToken: cancellationToken);
            var header = new ReportHeader { JobId = jobId, Format = report.Format, Name = report.Name, Path = relative, Size = (ulong)content.Length };
            try
            {
                await call.RequestStream.WriteAsync(new UploadReportRequest { Header = header }, cancellationToken).ConfigureAwait(false);
                var buffer = new byte[ChunkSize];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await call.RequestStream.WriteAsync(new UploadReportRequest { Content = ByteString.CopyFrom(buffer, 0, read) }, cancellationToken).ConfigureAwait(false);
                }

                await call.RequestStream.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is RpcException or InvalidOperationException)
            {
                // The server answered before the file was through; its answer says why.
            }

            await call.ResponseAsync.ConfigureAwait(false);
        }
    }
}
