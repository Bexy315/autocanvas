using AutoCanvas.Core.Secrets;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Connection;
using Google.Protobuf.WellKnownTypes;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Collects a job's log lines into numbered chunks (runner protocol, <em>Logs</em>): one sequence for
/// setup and steps, a chunk at least every half second while lines arrive and before it reaches
/// 64 KiB, each chunk from one source (setup or one step), every line masked before it leaves.
/// </summary>
internal sealed class LogBatcher : IAsyncDisposable
{
    public const int MaxChunkBytes = 64 * 1024;

    /// <summary>Longer lines are split; the server would cut them.</summary>
    public const int MaxLineLength = 16 * 1024;

    public static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    private readonly string jobId;
    private readonly Outbox outbox;
    private readonly TimeProvider time;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task ticking;
    private LogChunk? current;
    private int currentBytes;
    private ulong sequence;

    public LogBatcher(string jobId, Outbox outbox, TimeProvider time)
    {
        this.jobId = jobId;
        this.outbox = outbox;
        this.time = time;
        ticking = TickAsync();
    }

    /// <summary>Chunks the outbox had no room for.</summary>
    public int Dropped { get; private set; }

    /// <summary>Adds a line of the setup (<paramref name="step"/> <see langword="null"/>) or of a step.</summary>
    public void Write(int? step, LogStream stream, string text, SecretRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        var masked = redactor.Redact(text);
        lock (gate)
        {
            if (current is not null && Source(current) != step)
            {
                FlushLocked();
            }

            foreach (var piece in Pieces(masked))
            {
                current ??= New(step);
                current.Lines.Add(new LogLine { Time = Timestamp.FromDateTimeOffset(time.GetUtcNow()), Stream = stream, Text = piece });
                currentBytes += piece.Length;
                if (currentBytes >= MaxChunkBytes)
                {
                    FlushLocked();
                }
            }
        }
    }

    /// <summary>Sends what has been collected, before a message that must follow the lines.</summary>
    public void Flush()
    {
        lock (gate)
        {
            FlushLocked();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync().ConfigureAwait(false);
        await ticking.ConfigureAwait(false);
        stop.Dispose();
        Flush();
    }

    private static IEnumerable<string> Pieces(string line)
    {
        if (line.Length <= MaxLineLength)
        {
            yield return line;
            yield break;
        }

        for (var start = 0; start < line.Length; start += MaxLineLength)
        {
            yield return line.Substring(start, Math.Min(MaxLineLength, line.Length - start));
        }
    }

    private static int? Source(LogChunk chunk) => chunk.SourceCase == LogChunk.SourceOneofCase.StepIndex ? (int)chunk.StepIndex : null;

    private LogChunk New(int? step)
    {
        var chunk = new LogChunk { JobId = jobId, Sequence = sequence++ };
        if (step is { } index)
        {
            chunk.StepIndex = (uint)index;
        }
        else
        {
            chunk.JobSetup = true;
        }

        return chunk;
    }

    private void FlushLocked()
    {
        if (current is null)
        {
            return;
        }

        if (!outbox.SendLog(current))
        {
            Dropped++;
        }

        current = null;
        currentBytes = 0;
    }

    // The interval ticks on the wall clock; the lines' times come from the TimeProvider.
    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
            {
                Flush();
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }
}
