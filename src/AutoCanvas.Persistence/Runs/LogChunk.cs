namespace AutoCanvas.Persistence.Runs;

/// <summary>
/// A batch of log lines of a job attempt, as its runner sent it and masked again by the server
/// (ADR-0015). The sequence number is the runner's: unique per attempt, so a chunk sent twice after
/// a reconnect is stored once, and missing numbers show gaps.
/// </summary>
public sealed class LogChunk
{
    /// <summary>Column size of <see cref="Lines"/>.</summary>
    public const int MaxLinesLength = 1_048_576;

    /// <summary>The attempt.</summary>
    public required Guid AttemptId { get; init; }

    /// <summary>Position of the chunk in the attempt's log, from 0.</summary>
    public required long Sequence { get; init; }

    /// <summary>The step that wrote the lines; <see langword="null"/> for preparation and cleanup.</summary>
    public int? StepIndex { get; init; }

    /// <summary>The lines as a JSON array of objects with <c>time</c>, <c>stream</c> and <c>text</c>.</summary>
    public required string Lines { get; init; }

    /// <summary>How many lines the chunk has.</summary>
    public required int LineCount { get; init; }

    /// <summary>Size of the lines' text in bytes (UTF-8), counted against the job's log limit.</summary>
    public required int ByteCount { get; init; }

    /// <summary>When the server stored it.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Navigation to the attempt.</summary>
    public JobAttempt? Attempt { get; init; }
}
