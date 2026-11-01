namespace AutoCanvas.Runner.Connection;

/// <summary>
/// How long to wait before connecting again: doubling from one second to a minute, each wait cut by
/// up to a fifth at random, so that runners that lost the server together do not return together.
/// </summary>
internal sealed class Backoff(Func<double> random)
{
    public static readonly TimeSpan First = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(1);

    private TimeSpan next = First;

    public Backoff()
        : this(Random.Shared.NextDouble)
    {
    }

    /// <summary>The next wait; each one is twice the last, up to <see cref="Longest"/>.</summary>
    public TimeSpan Next()
    {
        var wait = next * (1 - (random() * 0.2));
        next = TimeSpan.FromTicks(Math.Min(next.Ticks * 2, Longest.Ticks));
        return wait;
    }

    /// <summary>Starts again from <see cref="First"/>, after the server welcomed the runner.</summary>
    public void Reset() => next = First;
}
