using AutoCanvas.TestSupport;

namespace AutoCanvas.Forge.Tests;

public sealed class GenericGitForgeTests : IAsyncLifetime
{
    private const string Build = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            steps:
              - run: make
        """;

    private readonly GenericGitForge forge = new(TimeSpan.FromSeconds(60));
    private readonly string cache = Path.Combine(Path.GetTempPath(), "autocanvas-forge-tests", Guid.NewGuid().ToString("N"));
    private GitHttpServer server = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        server = await GitHttpServer.StartAsync();
        await server.CreateAsync("shop");
    }

    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
        if (Directory.Exists(cache))
        {
            foreach (var file in Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(cache, recursive: true);
        }
    }

    private RepositoryAccess Access(string repository = "shop", string? user = null, string? secret = null) =>
        new(server.Url(repository), user, secret, cache);

    [Fact]
    public async Task The_default_branch_and_its_automation_files_are_read_at_the_newest_commit()
    {
        await server.CommitAsync("shop", new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [".autocanvas/automations/build.yml"] = Build,
            [".autocanvas/automations/deploy.yaml"] = Build,
            [".autocanvas/automations/notes.md"] = "not an automation",
            [".autocanvas/automations/nested/inner.yml"] = Build,
            [".autocanvas/settings.yml"] = "not an automation either",
            ["src/main.c"] = "int main() { return 0; }",
        }, "First version");
        var second = await server.CommitAsync("shop", new Dictionary<string, string?>(StringComparer.Ordinal) { [".autocanvas/automations/deploy.yaml"] = null }, "Drop deploy", author: "Grace Hopper");

        var head = await forge.ResolveAsync(Access(), branch: null, Cancel);
        var snapshot = await forge.ReadAutomationsAsync(Access(), head.Branch, Cancel);

        head.ShouldBe(new ResolvedBranch("main", second));
        snapshot.Commit.ShouldBe(second);
        snapshot.Subject.ShouldBe("Drop deploy");
        snapshot.Author.ShouldBe("Grace Hopper");
        snapshot.Files.Select(f => (f.Path, f.Name)).ShouldBe([(".autocanvas/automations/build.yml", "build")]);
        snapshot.Files[0].Content.ShouldBe(Build);
        forge.Kind.ShouldBe(ForgeKind.GenericGit);
        forge.Capabilities.ShouldBe(ForgeCapabilities.None);
    }

    [Fact]
    public async Task Another_branch_is_read_on_its_own_and_unknown_ones_are_named()
    {
        await server.CommitAsync("shop", new Dictionary<string, string?>(StringComparer.Ordinal) { ["README.md"] = "shop" }, "Start");
        var feature = await server.CommitAsync("shop", new Dictionary<string, string?>(StringComparer.Ordinal) { [".autocanvas/automations/build.yml"] = Build }, "Add build", branch: "feature/build");

        (await forge.ResolveAsync(Access(), "feature/build", Cancel)).Commit.ShouldBe(feature);
        (await forge.ReadAutomationsAsync(Access(), "feature/build", Cancel)).Files.Count.ShouldBe(1);
        (await forge.ReadAutomationsAsync(Access(), "main", Cancel)).Files.ShouldBeEmpty();

        (await Should.ThrowAsync<ForgeException>(() => forge.ResolveAsync(Access(), "nope", Cancel))).Message.ShouldContain("'nope' does not exist");
        (await Should.ThrowAsync<ForgeException>(() => forge.ReadAutomationsAsync(Access(), "--upload-pack=evil", Cancel))).Message.ShouldContain("is not a branch name");
    }

    [Fact]
    public async Task An_empty_repository_has_no_default_branch_yet()
    {
        (await Should.ThrowAsync<ForgeException>(() => forge.ResolveAsync(Access(), branch: null, Cancel))).Message.ShouldContain("no default branch yet");
    }

    [Fact]
    public async Task A_protected_repository_needs_its_credential_which_never_shows_in_a_message()
    {
        await using var guarded = await GitHttpServer.StartAsync(("ci", "s3cret-token"));
        await guarded.CreateAsync("private");
        await guarded.CommitAsync("private", new Dictionary<string, string?>(StringComparer.Ordinal) { [".autocanvas/automations/build.yml"] = Build }, "Start");
        RepositoryAccess Guarded(string? secret) => new(guarded.Url("private"), "ci", secret, cache);

        var refused = await Should.ThrowAsync<ForgeException>(() => forge.ResolveAsync(Guarded("wrong-token"), branch: null, Cancel));
        refused.Message.ShouldStartWith("git: ");
        refused.Message.ShouldNotContain("wrong-token");

        var head = await forge.ResolveAsync(Guarded("s3cret-token"), branch: null, Cancel);
        (await forge.ReadAutomationsAsync(Guarded("s3cret-token"), head.Branch, Cancel)).Files.Count.ShouldBe(1);
        Guarded("s3cret-token").ToString().ShouldNotContain("s3cret-token");
    }

    [Fact]
    public async Task Branches_and_tags_are_listed_with_their_commits_and_annotated_tags_with_the_commit_they_tag()
    {
        var first = await server.CommitAsync("shop", Files(("README.md", "one")), "First");
        var feature = await server.CommitAsync("shop", Files(("README.md", "two")), "Feature", branch: "feature/x");
        await server.TagAsync("shop", "v1", first);
        await server.TagAsync("shop", "v1.1", first, message: "Release 1.1");

        var refs = await forge.ListRefsAsync(Access(), Cancel);

        refs.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["refs/heads/main"] = first,
            ["refs/heads/feature/x"] = feature,
            ["refs/tags/v1"] = first,
            ["refs/tags/v1.1"] = first,
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Automation_files_are_read_at_a_tag_or_a_full_branch_ref()
    {
        var first = await server.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "First");
        await server.CommitAsync("shop", Files((".autocanvas/automations/build.yml", null)), "Drop it");
        await server.TagAsync("shop", "v1", first, message: "One");

        var tagged = await forge.ReadAutomationsAsync(Access(), "refs/tags/v1", Cancel);

        tagged.Commit.ShouldBe(first);
        tagged.Files.ShouldHaveSingleItem().Path.ShouldBe(".autocanvas/automations/build.yml");
        (await forge.ReadAutomationsAsync(Access(), "refs/heads/main", Cancel)).Files.ShouldBeEmpty();
        (await Should.ThrowAsync<ForgeException>(() => forge.ReadAutomationsAsync(Access(), "refs/pull/1/head", Cancel))).Message.ShouldContain("is not a branch or tag");
    }

    [Fact]
    public async Task The_changed_files_between_two_commits_are_known_unless_the_older_one_is_not_there()
    {
        var first = await server.CommitAsync("shop", Files(("src/a.c", "a"), ("README.md", "one")), "First");
        await server.CommitAsync("shop", Files(("src/b.c", "b")), "Second");
        var third = await server.CommitAsync("shop", Files(("README.md", null), ("docs/x.md", "x")), "Third");

        var changed = await forge.ChangedFilesAsync(Access(), "main", first, third, Cancel);

        changed.ShouldBe(["README.md", "docs/x.md", "src/b.c"], ignoreOrder: true);
        (await forge.ChangedFilesAsync(Access(), "main", new string('0', 40), third, Cancel)).ShouldBeNull();
        (await forge.ChangedFilesAsync(Access(), "main", new string('a', 40), third, Cancel)).ShouldBeNull();
        (await forge.ChangedFilesAsync(Access(), "main", "--output=x", third, Cancel)).ShouldBeNull();
    }

    private static Dictionary<string, string?> Files(params (string Path, string? Content)[] files) =>
        files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

    [Fact]
    public async Task Only_http_and_https_repositories_are_read()
    {
        var local = new RepositoryAccess(new Uri("file:///etc"), null, null, cache);

        (await Should.ThrowAsync<ForgeException>(() => forge.ResolveAsync(local, null, Cancel))).Message.ShouldContain("http or https");
    }

    [Theory]
    [InlineData(".autocanvas/automations/build.yml", true)]
    [InlineData(".autocanvas/automations/build.yaml", true)]
    [InlineData(".autocanvas/automations/build.json", false)]
    [InlineData(".autocanvas/automations/team/build.yml", false)]
    [InlineData(".autocanvas/build.yml", false)]
    [InlineData("other/.autocanvas/automations/build.yml", false)]
    public void Only_files_directly_in_the_automations_directory_count(string path, bool counts) =>
        AutomationPaths.IsAutomation(path).ShouldBe(counts);

    [Fact]
    public void Git_runs_without_prompts_and_carries_the_credential_as_a_header()
    {
        var environment = GitCli.Environment(new RepositoryAccess(new Uri("https://example.org/r.git"), null, "token", cache));

        environment["GIT_TERMINAL_PROMPT"].ShouldBe("0");
        environment["GIT_ALLOW_PROTOCOL"].ShouldBe("http:https");
        environment["GIT_CONFIG_VALUE_0"].ShouldBe($"Authorization: Basic {Convert.ToBase64String("x-access-token:token"u8)}");
        GitCli.Environment(null).ShouldNotContainKey("GIT_CONFIG_COUNT");
        GitCli.Describe(string.Empty, null).ShouldBe("git failed without saying why.");
    }
}
