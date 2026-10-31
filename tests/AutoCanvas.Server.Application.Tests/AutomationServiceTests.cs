using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class AutomationServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Hello = """
        version: 1
        jobs:
          hello:
            steps:
              - run: echo hello
        """;

    private const string HelloTitled = """
        version: 1
        name: Say hello
        description: Greets everyone.
        jobs:
          hello:
            steps:
              - run: echo hello, world
        """;

    private static readonly string Examples = Path.Combine(AppContext.BaseDirectory, "examples");

    [Fact]
    public async Task Every_valid_example_automation_is_saved_with_its_title()
    {
        var ada = await ActorAsync("ada");
        var folder = await FolderAsync(ada, "Team");

        foreach (var file in Directory.GetFiles(Examples, "*.yml"))
        {
            var yaml = await File.ReadAllTextAsync(file, Cancel);
            var created = (await AutomationsAsync(a => a.CreateAsync(ada, folder, Path.GetFileNameWithoutExtension(file), yaml, null, Cancel))).ShouldSucceed();

            created.CurrentVersion.ShouldBe(1);
            string.Equals(created.Title, created.Name, StringComparison.Ordinal).ShouldBeFalse();
            created.Yaml.ShouldBe(yaml);
        }

        (await ReadAsync(db => db.Automations.CountAsync(Cancel))).ShouldBe(Directory.GetFiles(Examples, "*.yml").Length);
    }

    [Fact]
    public async Task Invalid_files_are_refused_with_every_problem_and_where_it_is()
    {
        var ada = await ActorAsync("ada");
        var folder = await FolderAsync(ada, "Team");

        foreach (var file in Directory.GetFiles(Path.Combine(Examples, "invalid"), "*.yml"))
        {
            var yaml = await File.ReadAllTextAsync(file, Cancel);
            var error = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "broken", yaml, null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);

            error.Field.ShouldBe("yaml");
            error.Diagnostics.ShouldNotBeNull().ShouldNotBeEmpty();
            error.Diagnostics.ShouldContain(d => d.Code.StartsWith("AC", StringComparison.Ordinal));
        }

        var unknownNeed = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "broken", Hello + "\n    needs: [nope]\n", null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        unknownNeed.Diagnostics!.ShouldContain(d => d.Location != null && d.Location.Value.Line > 0);
        (await ReadAsync(db => db.Automations.CountAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Saving_adds_versions_and_refuses_an_unchanged_file()
    {
        var ada = await ActorAsync("ada");
        var folder = await FolderAsync(ada, "Team");
        var created = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "hello", Hello, "first", Cancel))).ShouldSucceed();
        Clock.Advance(TimeSpan.FromMinutes(1));

        var saved = (await AutomationsAsync(a => a.SaveAsync(ada, created.Id, HelloTitled, " greet everyone ", expectedVersion: 1, Cancel))).ShouldSucceed();
        var unchanged = (await AutomationsAsync(a => a.SaveAsync(ada, created.Id, HelloTitled, null, null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);

        created.Title.ShouldBe("hello");
        saved.CurrentVersion.ShouldBe(2);
        saved.Title.ShouldBe("Say hello");
        saved.Description.ShouldBe("Greets everyone.");
        saved.UpdatedAt.ShouldBe(Clock.GetUtcNow());
        unchanged.Message.ShouldContain("version 2");

        var history = (await AutomationsAsync(a => a.ListVersionsAsync(ada, created.Id, new PageRequest(), Cancel))).ShouldSucceed();
        history.Items.Select(v => (v.Number, v.Message, v.CreatedByName)).ShouldBe([(2, "greet everyone", "ada"), (1, "first", "ada")]);
        (await AutomationsAsync(a => a.GetVersionAsync(ada, created.Id, 1, Cancel))).ShouldSucceed().Yaml.ShouldBe(Hello);
        (await AutomationsAsync(a => a.GetVersionAsync(ada, created.Id, 3, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AuditAsync(AuditActions.AutomationSaved)).ShouldHaveSingleItem().Details.ShouldNotBeNull().ShouldNotContain("echo");
    }

    [Fact]
    public async Task Viewers_read_editors_change_and_others_see_nothing()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var eve = await ActorAsync("eve");
        var folder = await FolderAsync(ada, "Team");
        (await FolderGrantsAsync(g => g.SetAsync(ada, folder, bob.UserId, null, FolderRole.Viewer, Cancel))).ShouldSucceed();
        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "hello", Hello, null, Cancel))).ShouldSucceed();

        (await AutomationsAsync(a => a.GetAsync(bob, hello.Id, Cancel))).ShouldSucceed().MyRole.ShouldBe(FolderRole.Viewer);
        (await AutomationsAsync(a => a.ListAsync(bob, folder, new PageRequest(), Cancel))).ShouldSucceed().Items.ShouldHaveSingleItem();
        (await AutomationsAsync(a => a.ListVersionsAsync(bob, hello.Id, new PageRequest(), Cancel))).ShouldSucceed();
        (await AutomationsAsync(a => a.SaveAsync(bob, hello.Id, HelloTitled, null, null, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await AutomationsAsync(a => a.CreateAsync(bob, folder, "mine", Hello, null, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await AutomationsAsync(a => a.DeleteAsync(bob, hello.Id, null, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await AutomationsAsync(a => a.GetAsync(eve, hello.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AutomationsAsync(a => a.GetVersionAsync(eve, hello.Id, 1, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AutomationsAsync(a => a.ListAsync(eve, folder, new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AutomationsAsync(a => a.GetAsync(ada, Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Names_are_unique_per_folder_and_renaming_keeps_the_versions()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var other = await FolderAsync(ada, "Other");
        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, team, "hello", Hello, null, Cancel))).ShouldSucceed();
        await AutomationsAsync(a => a.CreateAsync(ada, team, "deploy", Hello, null, Cancel));

        (await AutomationsAsync(a => a.CreateAsync(ada, team, "HELLO", Hello, null, Cancel))).ShouldFail(UseCaseErrorKind.Conflict).Field.ShouldBe("name");
        (await AutomationsAsync(a => a.CreateAsync(ada, other, "hello", Hello, null, Cancel))).ShouldSucceed();
        (await AutomationsAsync(a => a.CreateAsync(ada, team, "a/b", Hello, null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await AutomationsAsync(a => a.CreateAsync(ada, team, "long", Hello, new string('m', 501), Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("message");
        (await AutomationsAsync(a => a.RenameAsync(ada, hello.Id, "Deploy", null, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);

        var renamed = (await AutomationsAsync(a => a.RenameAsync(ada, hello.Id, "greet", null, Cancel))).ShouldSucceed();
        (await AutomationsAsync(a => a.RenameAsync(ada, hello.Id, "greet", null, Cancel))).ShouldSucceed();

        renamed.Name.ShouldBe("greet");
        renamed.Title.ShouldBe("greet");
        renamed.CurrentVersion.ShouldBe(1);
        (await AuditAsync(AuditActions.AutomationRenamed)).ShouldHaveSingleItem();
        (await AutomationsAsync(a => a.RenameAsync(ada, hello.Id, " ", null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task Moving_needs_the_editor_role_on_both_folders_and_a_free_name()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = await FolderAsync(ada, "Team");
        var archive = await FolderAsync(ada, "Archive");
        var bobs = await FolderAsync(bob, "Bobs");
        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, team, "hello", Hello, null, Cancel))).ShouldSucceed();
        await AutomationsAsync(a => a.CreateAsync(ada, archive, "Hello", Hello, null, Cancel));

        (await AutomationsAsync(a => a.MoveAsync(ada, hello.Id, bobs, null, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AutomationsAsync(a => a.MoveAsync(ada, hello.Id, archive, null, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        await AutomationsAsync(a => a.RenameAsync(ada, hello.Id, "hello-old", null, Cancel));
        var moved = (await AutomationsAsync(a => a.MoveAsync(ada, hello.Id, archive, null, Cancel))).ShouldSucceed();
        (await AutomationsAsync(a => a.MoveAsync(ada, hello.Id, archive, null, Cancel))).ShouldSucceed();

        moved.FolderId.ShouldBe(archive);
        (await AuditAsync(AuditActions.AutomationMoved)).ShouldHaveSingleItem().FolderId.ShouldBe(archive);
    }

    [Fact]
    public async Task Changes_against_an_old_version_are_refused()
    {
        var ada = await ActorAsync("ada");
        var folder = await FolderAsync(ada, "Team");
        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "hello", Hello, null, Cancel))).ShouldSucceed();
        await AutomationsAsync(a => a.SaveAsync(ada, hello.Id, HelloTitled, null, null, Cancel));

        (await AutomationsAsync(a => a.SaveAsync(ada, hello.Id, Hello, null, expectedVersion: 1, Cancel))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
        (await AutomationsAsync(a => a.RenameAsync(ada, hello.Id, "x", expectedVersion: 1, Cancel))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
        (await AutomationsAsync(a => a.MoveAsync(ada, hello.Id, folder, expectedVersion: 1, Cancel))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
        (await AutomationsAsync(a => a.DeleteAsync(ada, hello.Id, expectedVersion: 1, Cancel))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
        (await AutomationsAsync(a => a.SaveAsync(ada, hello.Id, Hello, "restore version 1", expectedVersion: 2, Cancel))).ShouldSucceed().CurrentVersion.ShouldBe(3);
    }

    [Fact]
    public async Task Versions_keep_their_author_after_the_user_is_deleted()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var folder = await FolderAsync(ada, "Team");
        (await FolderGrantsAsync(g => g.SetAsync(ada, folder, bob.UserId, null, FolderRole.Editor, Cancel))).ShouldSucceed();
        var hello = (await AutomationsAsync(a => a.CreateAsync(bob, folder, "hello", Hello, null, Cancel))).ShouldSucceed();

        (await UsersAsync(users => users.DeleteAsync(bob.UserId, Cancel))).ShouldSucceed();

        var version = (await AutomationsAsync(a => a.GetVersionAsync(ada, hello.Id, 1, Cancel))).ShouldSucceed();
        version.CreatedById.ShouldBeNull();
        version.CreatedByName.ShouldBe("bob");
    }

    [Fact]
    public async Task Deleting_removes_every_version_and_frees_the_folder()
    {
        var ada = await ActorAsync("ada");
        var folder = await FolderAsync(ada, "Team");
        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "hello", Hello, null, Cancel))).ShouldSucceed();
        await AutomationsAsync(a => a.SaveAsync(ada, hello.Id, HelloTitled, null, null, Cancel));

        (await FoldersAsync(f => f.DeleteAsync(ada, folder, null, Cancel))).ShouldFail(UseCaseErrorKind.Conflict).Message.ShouldContain("automations");
        (await AutomationsAsync(a => a.DeleteAsync(ada, hello.Id, expectedVersion: 2, Cancel))).ShouldSucceed();

        (await ReadAsync(db => db.AutomationVersions.CountAsync(Cancel))).ShouldBe(0);
        (await AuditAsync(AuditActions.AutomationDeleted)).ShouldHaveSingleItem().FolderId.ShouldBe(folder);
        (await FoldersAsync(f => f.DeleteAsync(ada, folder, null, Cancel))).ShouldSucceed();
    }

    [Fact]
    public async Task Lists_and_histories_are_paged()
    {
        var ada = await ActorAsync("ada");
        var folder = await FolderAsync(ada, "Team");
        foreach (var name in new[] { "c", "A", "b" })
        {
            await AutomationsAsync(a => a.CreateAsync(ada, folder, name, Hello, null, Cancel));
        }

        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "hello", Hello, null, Cancel))).ShouldSucceed();
        await AutomationsAsync(a => a.SaveAsync(ada, hello.Id, HelloTitled, null, null, Cancel));
        await AutomationsAsync(a => a.SaveAsync(ada, hello.Id, Hello, null, null, Cancel));

        var first = (await AutomationsAsync(a => a.ListAsync(ada, folder, new PageRequest(2), Cancel))).ShouldSucceed();
        var second = (await AutomationsAsync(a => a.ListAsync(ada, folder, new PageRequest(2, first.Next), Cancel))).ShouldSucceed();
        var newest = (await AutomationsAsync(a => a.ListVersionsAsync(ada, hello.Id, new PageRequest(2), Cancel))).ShouldSucceed();
        var oldest = (await AutomationsAsync(a => a.ListVersionsAsync(ada, hello.Id, new PageRequest(2, newest.Next), Cancel))).ShouldSucceed();

        first.Items.Select(a => a.Name).ShouldBe(["A", "b"]);
        second.Items.Select(a => a.Name).ShouldBe(["c", "hello"]);
        newest.Items.Select(v => v.Number).ShouldBe([3, 2]);
        oldest.Items.Select(v => v.Number).ShouldBe([1]);
        oldest.Next.ShouldBeNull();
        (await AutomationsAsync(a => a.ListVersionsAsync(ada, hello.Id, new PageRequest(2, "x"), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await AutomationsAsync(a => a.ListVersionsAsync(ada, hello.Id, new PageRequest(0), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await AutomationsAsync(a => a.ListAsync(ada, folder, new PageRequest(0), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    private async Task<Guid> FolderAsync(Actor owner, string name) =>
        (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder(name), Cancel))).ShouldSucceed().Id;
}

public sealed class AutomationServiceTestsOnSqlite() : AutomationServiceTests(DatabaseProvider.Sqlite);

public sealed class AutomationServiceTestsOnPostgres() : AutomationServiceTests(DatabaseProvider.Postgres);
