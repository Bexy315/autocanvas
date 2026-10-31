using AutoCanvas.Protocol.Runner.V1;
using Grpc.Core;
using Grpc.Net.Client;

namespace AutoCanvas.Server.Tests.Runners;

/// <summary>
/// A runner as the tests play it: the generated gRPC clients over the in-memory test server, plus
/// helpers to register, connect and read the control stream with a time limit.
/// </summary>
internal sealed class ProtocolClient : IDisposable
{
    public ProtocolClient(AutoCanvasFactory factory) =>
        Channel = GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });

    public GrpcChannel Channel { get; }

    public static Capabilities Capabilities(uint maxJobs = 1, params string[] labels)
    {
        var capabilities = new Capabilities
        {
            Executor = Executor.Container,
            Os = "linux",
            Arch = "amd64",
            MaxConcurrentJobs = maxJobs,
            RunnerVersion = "protocol-client/1.0",
        };
        capabilities.Labels.Add(labels.Length == 0 ? ["linux"] : labels);
        capabilities.Shells.Add("bash");
        return capabilities;
    }

    public Task<RegisterResponse> RegisterAsync(string token, string name, Capabilities? capabilities = null) =>
        new RegistrationService.RegistrationServiceClient(Channel)
            .RegisterAsync(new RegisterRequest { RegistrationToken = token, Name = name, Capabilities = capabilities ?? Capabilities() }, cancellationToken: TestContext.Current.CancellationToken)
            .ResponseAsync;

    /// <summary>Opens the control stream with the credential and sends Hello; nothing is read yet.</summary>
    public Task<RunnerStream> OpenAsync(string? credential, Hello? hello = null) =>
        OpenWithAsync(credential, new ConnectRequest { Hello = hello ?? Hello() });

    /// <summary>Opens the control stream and sends <paramref name="first"/>, whatever it is.</summary>
    public async Task<RunnerStream> OpenWithAsync(string? credential, ConnectRequest first)
    {
        var call = new RunnerService.RunnerServiceClient(Channel).Connect(Authorization(credential), cancellationToken: TestContext.Current.CancellationToken);
        var stream = new RunnerStream(call);
        try
        {
            await stream.SendAsync(first);
        }
        catch (RpcException)
        {
            // The server refused the call before the message went out; EndAsync reports why.
        }

        return stream;
    }

    /// <summary>The metadata that carries a runner credential.</summary>
    public static Metadata Authorization(string? credential)
    {
        var headers = new Metadata();
        if (credential is not null)
        {
            headers.Add("authorization", $"Bearer {credential}");
        }

        return headers;
    }

    /// <summary>Opens the control stream and reads the server's Welcome.</summary>
    public async Task<(RunnerStream Stream, Welcome Welcome)> ConnectAsync(string credential, Hello? hello = null)
    {
        var stream = await OpenAsync(credential, hello);
        var welcome = await stream.ReadAsync();
        welcome.MessageCase.ShouldBe(ConnectResponse.MessageOneofCase.Welcome);
        return (stream, welcome.Welcome);
    }

    public static Hello Hello(Capabilities? capabilities = null, params string[] activeJobIds)
    {
        var hello = new Hello { ProtocolVersion = "1.0", Capabilities = capabilities ?? Capabilities() };
        hello.ActiveJobIds.Add(activeJobIds);
        return hello;
    }

    public void Dispose() => Channel.Dispose();
}

/// <summary>One control stream from the runner's side.</summary>
internal sealed class RunnerStream(AsyncDuplexStreamingCall<ConnectRequest, ConnectResponse> call) : IDisposable
{
    /// <summary>Long enough for a busy CI machine, short enough to fail a test quickly.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private Task<bool>? pending;

    public Task SendAsync(ConnectRequest message) => call.RequestStream.WriteAsync(message, TestContext.Current.CancellationToken);

    public Task HeartbeatAsync(params string[] activeJobIds)
    {
        var heartbeat = new Heartbeat();
        heartbeat.ActiveJobIds.Add(activeJobIds);
        return SendAsync(new ConnectRequest { Heartbeat = heartbeat });
    }

    /// <summary>The next message; fails when none comes within <see cref="Patience"/> or the stream ends.</summary>
    public async Task<ConnectResponse> ReadAsync() =>
        await TryReadAsync(Patience) ?? throw new ShouldAssertException("The server sent nothing in time.");

    /// <summary>
    /// The next message, or <see langword="null"/> when none comes within <paramref name="wait"/>.
    /// A read that timed out stays pending, so the message it gets later is not lost.
    /// </summary>
    public async Task<ConnectResponse?> TryReadAsync(TimeSpan wait)
    {
        pending ??= call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);
        if (await Task.WhenAny(pending, Task.Delay(wait, TestContext.Current.CancellationToken)) != pending)
        {
            return null;
        }

        var more = await pending;
        pending = null;
        return more ? call.ResponseStream.Current : throw new ShouldAssertException("The server ended the stream.");
    }

    /// <summary>Whether the server ends the stream, after any messages still on the way.</summary>
    public async Task<StatusCode> EndAsync()
    {
        try
        {
            while (await (pending ?? call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)).WaitAsync(Patience, TestContext.Current.CancellationToken))
            {
                pending = null;
            }

            return StatusCode.OK;
        }
        catch (RpcException exception)
        {
            return exception.StatusCode;
        }
    }

    public void Dispose() => call.Dispose();
}
