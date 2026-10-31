using System.Text;
using System.Text.Json;
using AutoCanvas.Core.Secrets;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Stores the log lines runners send (ADR-0015): masked again with the run's secrets, each chunk
/// once per sequence number, long lines shortened, and no more per attempt than
/// <see cref="LogOptions.MaxBytesPerJob"/>.
/// </summary>
public sealed class LogIngestion(AutoCanvasDbContext database, ConfigurationService configuration, LogSignal signal, IOptions<LogOptions> options, TimeProvider time)
{
    /// <summary>The longest line kept, in characters; longer ones are cut.</summary>
    public const int MaxLineLength = 16 * 1024;

    /// <summary>The most lines one chunk may have; the rest are dropped.</summary>
    public const int MaxLinesPerChunk = 5000;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>
    /// What masks the attempt's lines: every secret its run can read. A connection builds it once
    /// per attempt; <see langword="null"/> if the attempt does not exist.
    /// </summary>
    public async Task<SecretRedactor?> RedactorAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var context = await AttemptContext.LoadAsync(database, configuration, time, attemptId, track: false, cancellationToken).ConfigureAwait(false);
        return context is null ? null : new SecretRedactor(context.Values.Secrets.Values);
    }

    /// <summary>Stores a chunk of the runner's attempt.</summary>
    /// <returns>Whether it was stored: not a repeat, not after the limit, and the runner's own attempt.</returns>
    public async Task<bool> AppendAsync(Guid attemptId, Guid runnerId, ReceivedLogChunk chunk, SecretRedactor redactor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(redactor);
        var attempt = await database.JobAttempts.AsNoTracking()
            .Where(a => a.Id == attemptId && a.RunnerId == runnerId)
            .Select(a => new { a.LogBytes, a.LogTruncated })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (attempt is null || attempt.LogTruncated || chunk.Sequence < 0
            || await database.LogChunks.AnyAsync(c => c.AttemptId == attemptId && c.Sequence == chunk.Sequence, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var lines = chunk.Lines.Take(MaxLinesPerChunk).Select(l => l with { Text = Cut(redactor.Redact(l.Text)) }).ToList();
        if (chunk.Lines.Count > MaxLinesPerChunk)
        {
            lines.Add(new LogLine(lines[^1].Time, LogSource.System, $"[{chunk.Lines.Count - MaxLinesPerChunk} more lines of this chunk were dropped]"));
        }

        var bytes = lines.Sum(l => Encoding.UTF8.GetByteCount(l.Text));
        var limit = options.Value.MaxBytesPerJob;
        var truncated = attempt.LogBytes + bytes > limit;
        if (truncated)
        {
            lines = [new LogLine(time.GetUtcNow(), LogSource.System, $"[The log reached its limit of {limit / (1024 * 1024)} MiB; later lines were dropped.]")];
            bytes = 0;
        }

        var json = JsonSerializer.Serialize(lines, Json);
        if (json.Length > LogChunk.MaxLinesLength)
        {
            json = JsonSerializer.Serialize(new[] { new LogLine(time.GetUtcNow(), LogSource.System, "[This chunk was too large to keep.]") }, Json);
        }

        database.LogChunks.Add(new LogChunk
        {
            AttemptId = attemptId,
            Sequence = chunk.Sequence,
            StepIndex = chunk.StepIndex,
            Lines = json,
            LineCount = lines.Count,
            ByteCount = bytes,
            ReceivedAt = time.GetUtcNow(),
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The same chunk arrived twice at the same moment.
            return false;
        }

        await database.JobAttempts.Where(a => a.Id == attemptId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.LogBytes, a => a.LogBytes + bytes).SetProperty(a => a.LogTruncated, a => a.LogTruncated || truncated), cancellationToken)
            .ConfigureAwait(false);
        signal.Pulse();
        return true;
    }

    private static string Cut(string text) => text.Length > MaxLineLength ? text[..MaxLineLength] + " [line cut]" : text;
}
