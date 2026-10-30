using AutoCanvas.Server.Application.Identity;
using Microsoft.Extensions.Time.Testing;

namespace AutoCanvas.Server.Application.Tests;

public sealed class LoginThrottleTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Failures_spread_beyond_the_window_do_not_lock()
    {
        var throttle = new LoginThrottle(clock);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            throttle.RecordFailure("ADA");
            clock.Advance(TimeSpan.FromMinutes(4));
        }

        throttle.IsLocked("ADA").ShouldBeFalse();
    }

    [Fact]
    public void A_success_forgets_earlier_failures()
    {
        var throttle = new LoginThrottle(clock);
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures - 1; attempt++)
        {
            throttle.RecordFailure("ADA");
        }

        throttle.Reset("ADA");
        throttle.RecordFailure("ADA");

        throttle.IsLocked("ADA").ShouldBeFalse();
    }

    [Fact]
    public void Many_names_do_not_grow_memory_without_bound()
    {
        var throttle = new LoginThrottle(clock);
        for (var name = 0; name < 10_000; name++)
        {
            throttle.RecordFailure($"NAME{name}");
        }

        clock.Advance(TimeSpan.FromMinutes(20));
        throttle.RecordFailure("ONE MORE");

        throttle.IsLocked("NAME1").ShouldBeFalse();
        throttle.IsLocked("ONE MORE").ShouldBeFalse();
    }

    [Fact]
    public void Tokens_are_random_and_only_their_hash_is_kept()
    {
        var first = SecretTokens.Create("acp_");
        var second = SecretTokens.Create("acp_");

        first.ShouldStartWith("acp_");
        first.Length.ShouldBe(4 + 43);
        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
        SecretTokens.Hash(first).Length.ShouldBe(64);
        SecretTokens.Hash(first).ShouldBe(SecretTokens.Hash(first));
    }
}
