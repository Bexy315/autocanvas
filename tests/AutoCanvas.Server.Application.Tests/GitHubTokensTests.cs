using AutoCanvas.Forge.GitHub;
using AutoCanvas.Server.Application.Forges;

namespace AutoCanvas.Server.Application.Tests;

public sealed class GitHubTokensTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_token_is_used_until_five_minutes_before_it_expires_and_the_one_before_it_is_remembered()
    {
        var tokens = new GitHubTokens();
        tokens.Valid(7, Now).ShouldBeNull();

        tokens.Store(7, new GitHubToken("first", Now.AddHours(1)));
        tokens.Valid(7, Now.AddMinutes(54)).ShouldBe("first");
        tokens.Valid(7, Now.AddMinutes(56)).ShouldBeNull();

        tokens.Store(7, new GitHubToken("second", Now.AddHours(2)));
        tokens.Issued(7).ShouldBe(["second", "first"]);
        tokens.Store(7, new GitHubToken("second", Now.AddHours(2)));
        tokens.Issued(7).ShouldBe(["second", "first"]);
        tokens.Issued(8).ShouldBeEmpty();

        tokens.Clear();
        tokens.Issued(7).ShouldBeEmpty();
    }
}
