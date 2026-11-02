using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace AutoCanvas.Server.Application.Common;

/// <summary>
/// Where the previous page of a list ordered by time (newest first) ended: its last instant and how
/// many entries of that instant it covered. Entries of the same instant are told apart by that count,
/// so no id comparison is needed (not every provider can compare ids).
/// </summary>
internal sealed record TimeCursor(DateTimeOffset At, int SameInstant)
{
    public static string Encode(DateTimeOffset at, int sameInstant) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{at.UtcTicks}:{sameInstant}")));

    public static UseCaseResult<TimeCursor?> Decode(string? cursor)
    {
        if (cursor is null)
        {
            return UseCase.Success<TimeCursor?>(null);
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks <= DateTimeOffset.MaxValue.UtcTicks
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var same))
            {
                return UseCase.Success<TimeCursor?>(new TimeCursor(new DateTimeOffset(ticks, TimeSpan.Zero), same));
            }
        }
        catch (FormatException)
        {
            // Falls through to the error below.
        }

        return UseCase.Failure<TimeCursor?>(UseCaseError.Invalid("cursor", "cursor is not a value from a previous page."));
    }

    /// <summary>The cursor after a page whose last entry is at <paramref name="last"/>.</summary>
    public static string After(IEnumerable<DateTimeOffset> page, DateTimeOffset last, TimeCursor? previous) =>
        Encode(last, page.Count(at => at == last) + (previous is { } before && before.At == last ? before.SameInstant : 0));
}
