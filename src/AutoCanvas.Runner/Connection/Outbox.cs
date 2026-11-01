using System.Threading.Channels;
using AutoCanvas.Protocol.Runner.V1;

namespace AutoCanvas.Runner.Connection;

/// <summary>
/// Messages for the server, in order. Jobs put them here whether or not a stream is open; the open
/// stream sends them, and while there is none they wait. A message that was being written when a
/// stream broke may be lost: jobs send their state again after a reconnect, and lost log chunks
/// show as gaps.
/// </summary>
internal sealed class Outbox
{
    /// <summary>Log chunks waiting beyond this are dropped rather than held; the server shows the gap.</summary>
    public const int MaxPendingLogs = 2000;

    private readonly Channel<ConnectRequest> messages = Channel.CreateUnbounded<ConnectRequest>(new UnboundedChannelOptions { SingleReader = true });
    private int pendingLogs;

    public ChannelReader<ConnectRequest> Reader => messages.Reader;

    /// <summary>Whether <see cref="Close"/> was called: the stream ends once the rest is sent.</summary>
    public bool IsClosed { get; private set; }

    public void Send(ConnectRequest message)
    {
        ArgumentNullException.ThrowIfNull(message);
        messages.Writer.TryWrite(message);
    }

    /// <summary>Queues a log chunk unless too many wait already.</summary>
    /// <returns>Whether it was queued.</returns>
    public bool SendLog(LogChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (Interlocked.Increment(ref pendingLogs) > MaxPendingLogs)
        {
            Interlocked.Decrement(ref pendingLogs);
            return false;
        }

        messages.Writer.TryWrite(new ConnectRequest { LogChunk = chunk });
        return true;
    }

    /// <summary>Takes no more messages: what is queued is sent, then the stream is closed.</summary>
    public void Close()
    {
        IsClosed = true;
        messages.Writer.TryComplete();
    }

    /// <summary>Tells the outbox a message left it, so that log chunks are counted correctly.</summary>
    public void Taken(ConnectRequest message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.MessageCase == ConnectRequest.MessageOneofCase.LogChunk)
        {
            Interlocked.Decrement(ref pendingLogs);
        }
    }
}
