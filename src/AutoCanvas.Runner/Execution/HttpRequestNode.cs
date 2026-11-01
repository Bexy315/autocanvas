using System.Diagnostics;
using System.Globalization;
using System.Text;
using AutoCanvas.Core.Nodes;
using AutoCanvas.Protocol.Runner.V1;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// The <c>http_request</c> node: one HTTP request from the runner, with the response's status and
/// body (cut after <see cref="MaxBody"/>) as outputs. A status of 400 or more fails the step unless
/// <c>fail_on_status</c> is false.
/// </summary>
internal sealed class HttpRequestNode(HttpClient http)
{
    /// <summary>The most of a body kept as output; outputs are values, not files.</summary>
    public const int MaxBody = 32 * 1024;

    public async Task<NodeResult> RunAsync(NodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var inputs = context.Inputs;
        var url = inputs.RequiredText("url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            throw new NodeInputException($"The input 'url' must be an http or https address; it is '{url}'.");
        }

        var method = (inputs.Text("method") ?? "GET").ToUpperInvariant();
        if (!BuiltInNodeTypes.Methods.Contains(method, StringComparer.Ordinal))
        {
            throw new NodeInputException($"The input 'method' must be one of {string.Join(", ", BuiltInNodeTypes.Methods)}.");
        }

        var timeout = TimeSpan.FromSeconds(inputs.WholeNumber("timeout_seconds", 30, 1, 600));
        var failOnStatus = inputs.Boolean("fail_on_status", true);
        using var request = Request(method, uri, inputs.TextMap("headers"), inputs.Text("body"));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(context.Stop);
        limit.CancelAfter(timeout);
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            var (body, cut) = await BodyAsync(response, limit.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            context.Write(LogStream.System, $"{method} {uri} answered {status} {response.ReasonPhrase} after {watch.ElapsedMilliseconds} ms.");
            if (cut)
            {
                context.Write(LogStream.System, $"The body is longer than {MaxBody / 1024} KiB; the output 'body' keeps the start.");
            }

            var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["status"] = status.ToString(CultureInfo.InvariantCulture),
                ["body"] = body,
            };
            return failOnStatus && status >= 400
                ? NodeResult.Failed($"The server answered {status} {response.ReasonPhrase}.", outputs)
                : new NodeResult(true, outputs, null);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !context.Stop.IsCancellationRequested)
        {
            return NodeResult.Failed($"{method} {uri} got no answer within {timeout.TotalSeconds} seconds.");
        }
        catch (HttpRequestException exception)
        {
            return NodeResult.Failed($"{method} {uri} failed: {exception.Message}");
        }
    }

    private static HttpRequestMessage Request(string method, Uri uri, Dictionary<string, string> headers, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
        }

        foreach (var (name, value) in headers)
        {
            if (request.Headers.TryAddWithoutValidation(name, value))
            {
                continue;
            }

            // Content headers such as Content-Type belong to the body.
            request.Content ??= new ByteArrayContent([]);
            request.Content.Headers.Remove(name);
            request.Content.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    private static async Task<(string Body, bool Cut)> BodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[MaxBody + 1];
            var read = 0;
            int count;
            while (read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false)) > 0)
            {
                read += count;
            }

            var cut = read > MaxBody;
            return (Encoding.UTF8.GetString(buffer, 0, Math.Min(read, MaxBody)), cut);
        }
    }
}
