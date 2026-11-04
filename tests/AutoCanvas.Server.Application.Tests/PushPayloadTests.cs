using System.Text.Json;
using AutoCanvas.Server.Application.Repositories;

namespace AutoCanvas.Server.Application.Tests;

public sealed class PushPayloadTests
{
    private static readonly string A = new('a', 40);
    private static readonly string B = new('b', 40);
    private static readonly string[] X = ["x"];
    private static readonly string[] YX = ["y", "x"];
    private static readonly string[] Z = ["z"];

    [Fact]
    public void A_push_brings_its_ref_commits_files_pusher_and_message()
    {
        var push = Read(new
        {
            @ref = "refs/heads/main",
            before = A,
            after = B,
            pusher = new { name = "ada" },
            head_commit = new { message = "Fix" },
            commits = new[] { new { id = B, added = X, modified = YX, removed = Z } },
        })!;

        (push.Ref, push.Before, push.After, push.Pusher, push.Message).ShouldBe(("refs/heads/main", A, B, "ada", "Fix"));
        push.ChangedFiles.ShouldBe(["x", "y", "z"]);
    }

    [Fact]
    public void The_changed_files_are_not_known_for_new_refs_forced_pushes_and_long_or_cut_lists()
    {
        var commit = new { id = B, added = X };
        Read(new { @ref = "refs/heads/main", before = new string('0', 40), after = B, commits = new[] { commit } })!.ChangedFiles.ShouldBeNull();
        Read(new { @ref = "refs/heads/main", before = A, after = B, forced = true, commits = new[] { commit } })!.ChangedFiles.ShouldBeNull();
        Read(new { @ref = "refs/heads/main", before = A, after = B, commits = Array.Empty<object>() })!.ChangedFiles.ShouldBeNull();
        Read(new { @ref = "refs/heads/main", before = A, after = B, commits = Enumerable.Repeat(commit, PushPayload.MaxCommits).ToArray() })!.ChangedFiles.ShouldBeNull();
        Read(new { object_kind = "push", @ref = "refs/heads/main", before = A, after = B, total_commits_count = 30, commits = new[] { commit } })!.ChangedFiles.ShouldBeNull();
    }

    [Fact]
    public void GitLab_tag_pushes_name_the_pusher_and_take_the_message_of_the_pushed_commit()
    {
        var push = Read(new
        {
            object_kind = "tag_push",
            @ref = "refs/tags/v1",
            before = new string('0', 40),
            after = B,
            user_name = "Grace",
            commits = new[] { new { id = A, message = "older" }, new { id = B, message = "newest" } },
        })!;

        (push.Ref, push.Pusher, push.Message).ShouldBe(("refs/tags/v1", "Grace", "newest"));
    }

    [Theory]
    [InlineData("""{"object_kind":"merge_request","ref":"refs/heads/main"}""")]
    [InlineData("""{"ref":"refs/pull/1/head","before":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","after":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}""")]
    [InlineData("""{"ref":"refs/heads/main","before":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","after":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}""")]
    [InlineData("""{"ref":"refs/heads/main","before":"--upload-pack=x","after":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}""")]
    [InlineData("""{"ref":"refs/heads/main"}""")]
    [InlineData("""[1, 2]""")]
    public void Anything_but_a_push_with_a_branch_or_tag_and_two_commits_is_none(string json)
    {
        using var document = JsonDocument.Parse(json);

        PushPayload.Read(document.RootElement).ShouldBeNull();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("push", true)]
    [InlineData("Push Hook", true)]
    [InlineData("Tag Push Hook", true)]
    [InlineData("pull_request", false)]
    public void Events_are_pushes_by_their_name(string? eventName, bool push) => PushPayload.IsPush(eventName).ShouldBe(push);

    private static PushEvent? Read(object payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return PushPayload.Read(document.RootElement);
    }
}
