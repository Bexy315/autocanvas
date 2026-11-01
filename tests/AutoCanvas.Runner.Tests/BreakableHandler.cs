namespace AutoCanvas.Runner.Tests;

/// <summary>Passes requests to the test server and can break the open control streams, as a lost network would.</summary>
internal sealed class BreakableHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly List<CancellationTokenSource> streams = [];

    /// <summary>How many control streams the runner has opened.</summary>
    public int Connects { get; private set; }

    /// <summary>Whether the network is gone for good: new calls fail, as for a runner that was killed.</summary>
    public bool Blocked { get; private set; }

    /// <summary>Breaks the open streams and refuses every call from now on.</summary>
    public Task BlockAsync()
    {
        Blocked = true;
        return BreakAsync();
    }

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
        if (Blocked)
        {
            throw new HttpRequestException("The test cut this runner off the network.");
        }

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
