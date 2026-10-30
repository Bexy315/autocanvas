using System.Collections.Concurrent;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>
/// Slows down password guessing per account: after <see cref="MaxFailures"/> failed sign-ins
/// within <see cref="Window"/>, the name is locked for <see cref="LockDuration"/>. It counts names
/// whether or not they exist, so a lock reveals nothing. State is kept in memory, per server
/// instance; the per-address rate limit of the host applies on top.
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    /// <summary>Failed attempts that lock a name.</summary>
    public const int MaxFailures = 5;

    /// <summary>The period in which failures are counted.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>How long a locked name stays locked.</summary>
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    // Bounds memory if someone tries many different names; stale entries are dropped first.
    private const int MaxTrackedNames = 10_000;

    private readonly ConcurrentDictionary<string, Attempts> attempts = new(StringComparer.Ordinal);

    /// <summary>Whether sign-ins for the normalized name are refused right now.</summary>
    public bool IsLocked(string normalizedName) =>
        attempts.TryGetValue(normalizedName, out var state) && state.LockedUntil > time.GetUtcNow();

    /// <summary>Counts a failed sign-in for the normalized name.</summary>
    public void RecordFailure(string normalizedName)
    {
        var now = time.GetUtcNow();
        if (attempts.Count >= MaxTrackedNames)
        {
            Prune(now);
        }

        attempts.AddOrUpdate(
            normalizedName,
            _ => new Attempts(1, now, null),
            (_, state) =>
            {
                var current = now - state.WindowStart > Window ? new Attempts(0, now, null) : state;
                var failures = current.Failures + 1;
                return current with { Failures = failures, LockedUntil = failures >= MaxFailures ? now + LockDuration : current.LockedUntil };
            });
    }

    /// <summary>Forgets the failures of a name after a successful sign-in.</summary>
    public void Reset(string normalizedName) => attempts.TryRemove(normalizedName, out _);

    private void Prune(DateTimeOffset now)
    {
        foreach (var (name, state) in attempts)
        {
            if (now - state.WindowStart > Window && !(state.LockedUntil > now))
            {
                attempts.TryRemove(name, out _);
            }
        }
    }

    private sealed record Attempts(int Failures, DateTimeOffset WindowStart, DateTimeOffset? LockedUntil);
}
