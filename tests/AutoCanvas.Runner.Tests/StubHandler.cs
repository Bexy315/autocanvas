namespace AutoCanvas.Runner.Tests;

/// <summary>A web server for <c>http_request</c> tests: it records each request and answers as told.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } = _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK);

    /// <summary>How long it takes to answer; cancelling the request ends the wait.</summary>
    public TimeSpan Delay { get; set; }

    public List<(string Method, string Uri, List<string> Headers, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")
            .ToList();
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method.Method, request.RequestUri!.ToString(), headers, body));
        return Answer(request);
    }
}
