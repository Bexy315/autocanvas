using System.Globalization;

namespace AutoCanvas.Reports;

/// <summary>Consecutive line numbers, such as the lines no test reached.</summary>
/// <param name="Start">First line, from 1.</param>
/// <param name="End">Last line, equal to <paramref name="Start"/> for a single line.</param>
public sealed record LineRange(int Start, int End)
{
    /// <summary><c>12-15</c>, or <c>20</c> for a single line.</summary>
    /// <returns>The range as text.</returns>
    public override string ToString() =>
        Start == End ? Start.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{Start}-{End}");
}
