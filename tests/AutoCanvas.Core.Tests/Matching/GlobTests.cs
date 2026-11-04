using AutoCanvas.Core.Matching;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Tests.Matching;

public sealed class GlobTests
{
    [Theory]
    [InlineData("main", "main", true)]
    [InlineData("main", "mainline", false)]
    [InlineData("main", "Main", false)]
    [InlineData("release/*", "release/1.0", true)]
    [InlineData("release/*", "release/1.0/hotfix", false)]
    [InlineData("release/*", "release/", true)]
    [InlineData("release/**", "release/1.0/hotfix", true)]
    [InlineData("v?.?", "v1.2", true)]
    [InlineData("v?.?", "v1.23", false)]
    [InlineData("v?", "v/", false)]
    [InlineData("*", "feature/x", false)]
    [InlineData("**", "feature/x", true)]
    [InlineData("**", "", true)]
    [InlineData("", "", true)]
    [InlineData("", "a", false)]
    [InlineData("src/**/*.cs", "src/App.cs", true)]
    [InlineData("src/**/*.cs", "src/a/b/App.cs", true)]
    [InlineData("src/**/*.cs", "src/a/b/App.ts", false)]
    [InlineData("src/**", "src", false)]
    [InlineData("**/README.md", "README.md", true)]
    [InlineData("**/README.md", "docs/README.md", true)]
    [InlineData("*.md", "docs/README.md", false)]
    [InlineData("a*b*c", "aXbYc", true)]
    [InlineData("a*b*c", "aXbY", false)]
    public void Patterns_match_whole_names(string pattern, string text, bool matches)
    {
        Glob.IsMatch(pattern, text).ShouldBe(matches);
    }

    [Fact]
    public void Many_stars_do_not_take_long()
    {
        var text = new string('a', 2000) + "b";

        Glob.IsMatch("**a*a*a*a*a*a*a*a*a*a*c", text).ShouldBeFalse();
        Glob.MatchesAny(["x", "**b"], text).ShouldBeTrue();
    }

    [Theory]
    [InlineData("refs/heads/main", true)]
    [InlineData("refs/heads/release/2", true)]
    [InlineData("refs/heads/feature/x", false)]
    [InlineData("refs/tags/v1.0", true)]
    [InlineData("refs/tags/nightly", false)]
    [InlineData("refs/pull/1/head", false)]
    public void Push_triggers_match_branches_and_tags(string gitRef, bool matches)
    {
        new PushTrigger(["main", "release/*"], ["v*"], []).Matches(gitRef, changedFiles: null).ShouldBe(matches);
    }

    [Fact]
    public void Without_branches_every_branch_matches_and_without_tags_no_tag()
    {
        var push = new PushTrigger([], [], []);

        push.Matches("refs/heads/anything/at/all", []).ShouldBeTrue();
        push.Matches("refs/tags/v1", null).ShouldBeFalse();
    }

    [Fact]
    public void Paths_count_only_when_the_changed_files_are_known()
    {
        var push = new PushTrigger([], [], ["src/**"]);

        push.Matches("refs/heads/main", ["src/a/b.cs", "README.md"]).ShouldBeTrue();
        push.Matches("refs/heads/main", ["README.md"]).ShouldBeFalse();
        push.Matches("refs/heads/main", []).ShouldBeFalse();
        push.Matches("refs/heads/main", null).ShouldBeTrue();
    }
}
