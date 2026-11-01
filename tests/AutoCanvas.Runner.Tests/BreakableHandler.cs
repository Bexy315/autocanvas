namespace AutoCanvas.Runner.Tests;

/// <summary>Passes requests to the test server and can break the open control streams, as a lost network would.</summary>
internal sealed class BreakableHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly List<CancellationTokenSource> streams = [];

    /// <summary>How many control streams the runner has opened.</summary>
    public int Connects { get; private set; }

    public async Task BreakAsync()
    {
        CancellationTokenSource[] open;
        lock (streams)
        {
            open = [.. streams];
            streams.Clear();
        }

        foreach (var stream in open)
        {
            await stream.CancelAsync();
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.EndsWith("/Connect", StringComparison.Ordinal) != true)
        {
            return base.SendAsync(request, cancellationToken);
        }

        var broken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (streams)
        {
            Connects++;
            streams.Add(broken);
        }

        return base.SendAsync(request, broken.Token);
    }
}
