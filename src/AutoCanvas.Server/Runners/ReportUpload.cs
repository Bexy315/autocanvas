using System.IO.Pipelines;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Server.Application.Reports;
using Grpc.Core;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// One <c>UploadReport</c> call: the header is checked first, then the content is read into the
/// report parser while it arrives, through a pipe, so that neither memory nor disk holds the file.
/// </summary>
internal static class ReportUpload
{
    public static async Task<UploadReportResponse> ReceiveAsync(Guid runnerId, IAsyncStreamReader<UploadReportRequest> requests, ReportIngestion ingestion, ReportOptions options, CancellationToken cancellationToken)
    {
        if (!await requests.MoveNext(cancellationToken).ConfigureAwait(false) || requests.Current.PartCase != UploadReportRequest.PartOneofCase.Header)
        {
            throw Invalid("The first message of an upload must be the header.");
        }

        var file = ProtocolMapping.ToReportFile(requests.Current.Header)
            ?? throw Invalid("header.job_id must name a job and header.format must be set.");
        var begun = await ingestion.BeginAsync(runnerId, file, cancellationToken).ConfigureAwait(false);
        if (begun.Error is { } error)
        {
            throw ProtocolMapping.ToRpcException(error);
        }

        var receipt = begun.Value!;
        if (receipt.StoredId is { } stored)
        {
            return Response(stored);
        }

        var pipe = new Pipe();
        var storing = Task.Run(
            async () =>
            {
                var content = pipe.Reader.AsStream();
                await using (content.ConfigureAwait(false))
                {
                    // The parsers read synchronously; this task is the thread they block.
                    return await ingestion.StoreAsync(receipt, content, cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken);
        try
        {
            await CopyAsync(requests, pipe.Writer, options.MaxBytesPerFile, cancellationToken).ConfigureAwait(false);
            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RpcException or IOException or OperationCanceledException)
        {
            // The parser sees the failure as the end of its input; nothing of the file is stored.
            await pipe.Writer.CompleteAsync(exception).ConfigureAwait(false);
            await Task.WhenAny(storing).ConfigureAwait(false);
            _ = storing.Exception;
            throw;
        }

        return Response(await storing.ConfigureAwait(false));
    }

    private static async Task CopyAsync(IAsyncStreamReader<UploadReportRequest> requests, PipeWriter writer, long maxBytes, CancellationToken cancellationToken)
    {
        long received = 0;
        while (await requests.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            if (requests.Current.PartCase != UploadReportRequest.PartOneofCase.Content)
            {
                throw Invalid("Only the first message of an upload is a header.");
            }

            var content = requests.Current.Content;
            received += content.Length;
            if (received > maxBytes)
            {
                throw new RpcException(new Status(StatusCode.ResourceExhausted, $"The file is larger than {maxBytes} bytes, the most a report may have."));
            }

            var flushed = await writer.WriteAsync(content.Memory, cancellationToken).ConfigureAwait(false);
            if (flushed.IsCompleted)
            {
                // The parser stopped early: the file is not a report of its format, and the rest does not matter.
                return;
            }
        }
    }

    private static UploadReportResponse Response(Guid id) => new() { ReportId = id.ToString() };

    private static RpcException Invalid(string message) => new(new Status(StatusCode.InvalidArgument, message));
}
