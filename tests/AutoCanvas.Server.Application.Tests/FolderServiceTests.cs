using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class FolderServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    [Fact]
    public async Task Whoever_creates_a_top_level_folder_owns_it()
    {
        var ada = await ActorAsync("ada");
        var admin = await ActorAsync("root", isAdmin: true);

        var team = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder(" Team ", Description: "Our stuff"), Cancel))).ShouldSucceed();
        var shared = (await FoldersAsync(f => f.CreateAsync(admin, new NewFolder("Shared"), Cancel))).ShouldSucceed();

        team.Name.ShouldBe("Team");
        team.MyRole.ShouldBe(FolderRole.Owner);
        team.Path.ShouldBeEmpty();
        team.Version.ShouldBe(1);
        shared.MyRole.ShouldBe(FolderRole.Owner);
        var grants = await ReadAsync(db => db.FolderGrants.ToListAsync(Cancel));
        grants.ShouldHaveSingleItem().UserId.ShouldBe(ada.UserId);
        (await AuditAsync(AuditActions.FolderCreated)).Count.ShouldBe(2);
        (await AuditAsync(AuditActions.FolderGrantSet)).ShouldHaveSingleItem().FolderId.ShouldBe(team.Id);
    }

    [Fact]
    public async Task Subfolders_need_the_editor_role_on_the_parent_and_inherit_from_it()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var eve = await ActorAsync("eve");
        var team = await CreateAsync(ada, "Team");
        await GrantAsync(ada, team.Id, bob.UserId, FolderRole.Viewer);

        (await FoldersAsync(f => f.CreateAsync(bob, new NewFolder("Prod", team.Id), Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await FoldersAsync(f => f.CreateAsync(eve, new NewFolder("Prod", team.Id), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        var prod = await CreateAsync(ada, "Prod", team.Id);

        prod.Path.ShouldBe([new FolderCrumb(team.Id, "Team")]);
        (await FoldersAsync(f => f.GetAsync(bob, prod.Id, Cancel))).ShouldSucceed().MyRole.ShouldBe(FolderRole.Viewer);
        (await FoldersAsync(f => f.GetAsync(eve, prod.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(db => db.FolderGrants.CountAsync(Cancel))).ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("tab\there")]
    public async Task Folder_names_are_checked(string name)
    {
        var ada = await ActorAsync("ada");

        (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder(name), Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("name");
    }

    [Fact]
    public async Task Names_are_unique_among_siblings_regardless_of_case()
    {
        var ada = await ActorAsync("ada");
        var team = await CreateAsync(ada, "Team");
        var other = await CreateAsync(ada, "Other");
        await CreateAsync(ada, "Prod", team.Id);

        (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("PROD", team.Id), Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("team"), Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("Prod", other.Id), Cancel))).ShouldSucceed();
        (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("x", Description: new string('d', 501)), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task Folders_nest_at_most_sixteen_levels()
    {
        var ada = await ActorAsync("ada");
        Guid? parent = null;
        for (var level = 1; level <= 16; level++)
        {
            parent = (await CreateAsync(ada, $"level{level}", parent)).Id;
        }

        (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("too deep", parent), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task Breadcrumbs_show_names_but_only_the_folders_the_actor_may_open()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = await CreateAsync(ada, "Team");
        var prod = await CreateAsync(ada, "Prod", team.Id);
        var app = await CreateAsync(ada, "App", prod.Id);
        await GrantAsync(ada, prod.Id, bob.UserId, FolderRole.Viewer);

        var seen = (await FoldersAsync(f => f.GetAsync(bob, app.Id, Cancel))).ShouldSucceed();

        seen.Path.ShouldBe([new FolderCrumb(null, "Team"), new FolderCrumb(prod.Id, "Prod")]);
    }

    [Fact]
    public async Task Entry_points_are_the_topmost_folders_the_actor_may_view()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var admin = await ActorAsync("root", isAdmin: true);
        var team = await CreateAsync(ada, "Team");
        var prod = await CreateAsync(ada, "Prod", team.Id);
        var other = await CreateAsync(ada, "Other");
        var deep = await CreateAsync(ada, "Deep", other.Id);
        await GrantAsync(ada, team.Id, bob.UserId, FolderRole.Viewer);
        await GrantAsync(ada, prod.Id, bob.UserId, FolderRole.Editor);
        await GrantAsync(ada, deep.Id, bob.UserId, FolderRole.Viewer);

        var bobs = await FoldersAsync(f => f.ListEntryPointsAsync(bob, Cancel));
        var admins = await FoldersAsync(f => f.ListEntryPointsAsync(admin, Cancel));
        var eves = await FoldersAsync(async f => await f.ListEntryPointsAsync(await ActorAsync("eve"), Cancel));

        bobs.Select(f => f.Name).ShouldBe(["Deep", "Team"]);
        bobs[0].Path.ShouldBe([new FolderCrumb(null, "Other")]);
        admins.Select(f => f.Name).ShouldBe(["Other", "Team"]);
        eves.ShouldBeEmpty();
    }

    [Fact]
    public async Task Children_are_listed_by_name_with_the_role_on_each()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = await CreateAsync(ada, "Team");
        foreach (var name in new[] { "c", "A", "b", "secret" })
        {
            await CreateAsync(ada, name, team.Id);
        }

        var secret = (await FoldersAsync(f => f.ListChildrenAsync(ada, team.Id, new PageRequest(), Cancel))).ShouldSucceed().Items.Single(c => string.Equals(c.Name, "secret", StringComparison.Ordinal));
        await FoldersAsync(f => f.SetRestrictedAsync(ada, secret.Id, true, null, Cancel));
        var b = (await FoldersAsync(f => f.ListChildrenAsync(ada, team.Id, new PageRequest(), Cancel))).ShouldSucceed().Items.Single(c => string.Equals(c.Name, "b", StringComparison.Ordinal));
        await GrantAsync(ada, team.Id, bob.UserId, FolderRole.Viewer);
        await GrantAsync(ada, b.Id, bob.UserId, FolderRole.Editor);

        var first = (await FoldersAsync(f => f.ListChildrenAsync(bob, team.Id, new PageRequest(2), Cancel))).ShouldSucceed();
        var second = (await FoldersAsync(f => f.ListChildrenAsync(bob, team.Id, new PageRequest(2, first.Next), Cancel))).ShouldSucceed();

        first.Items.Select(c => (c.Name, c.MyRole)).ShouldBe([("A", FolderRole.Viewer), ("b", FolderRole.Editor)]);
        second.Items.Select(c => c.Name).ShouldBe(["c"]);
        second.Next.ShouldBeNull();
        (await FoldersAsync(f => f.ListChildrenAsync(ada, team.Id, new PageRequest(), Cancel))).ShouldSucceed().Items.Count.ShouldBe(4);
        (await FoldersAsync(f => f.ListChildrenAsync(bob, team.Id, new PageRequest(0), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await FoldersAsync(async f => await f.ListChildrenAsync(await ActorAsync("eve"), team.Id, new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Restricting_a_folder_cuts_inherited_roles_but_keeps_the_marker()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var carl = await ActorAsync("carl");
        var team = await CreateAsync(ada, "Team");
        var prod = await CreateAsync(ada, "Prod", team.Id);
        await GrantAsync(ada, team.Id, bob.UserId, FolderRole.Owner);
        await GrantAsync(ada, team.Id, carl.UserId, FolderRole.Editor);

        var restricted = (await FoldersAsync(f => f.SetRestrictedAsync(bob, prod.Id, true, null, Cancel))).ShouldSucceed();

        restricted.IsRestricted.ShouldBeTrue();
        restricted.Version.ShouldBe(2);
        (await FoldersAsync(f => f.GetAsync(bob, prod.Id, Cancel))).ShouldSucceed().MyRole.ShouldBe(FolderRole.Owner);
        (await FoldersAsync(f => f.GetAsync(carl, prod.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await FoldersAsync(f => f.GetAsync(ada, prod.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(db => db.FolderGrants.AnyAsync(g => g.FolderId == prod.Id && g.UserId == bob.UserId && g.Role == FolderRole.Owner, Cancel))).ShouldBeTrue();
        (await FoldersAsync(f => f.SetRestrictedAsync(carl, team.Id, true, null, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);

        (await FoldersAsync(f => f.SetRestrictedAsync(bob, prod.Id, false, null, Cancel))).ShouldSucceed();
        (await FoldersAsync(f => f.GetAsync(carl, prod.Id, Cancel))).ShouldSucceed().MyRole.ShouldBe(FolderRole.Editor);
        (await AuditAsync(AuditActions.FolderRestrictionChanged)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Renaming_needs_the_editor_role_a_free_name_and_the_current_version()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = await CreateAsync(ada, "Team");
        await CreateAsync(ada, "Taken");
        await GrantAsync(ada, team.Id, bob.UserId, FolderRole.Operator);

        var renamed = (await FoldersAsync(f => f.UpdateAsync(ada, team.Id, new FolderChanges { Name = "Squad", Description = "x" }, expectedVersion: 1, Cancel))).ShouldSucceed();
        await FoldersAsync(f => f.UpdateAsync(ada, team.Id, new FolderChanges { Name = "Squad" }, null, Cancel));

        renamed.Name.ShouldBe("Squad");
        renamed.Version.ShouldBe(2);
        (await AuditAsync(AuditActions.FolderUpdated)).ShouldHaveSingleItem();
        (await FoldersAsync(f => f.UpdateAsync(ada, team.Id, new FolderChanges { Name = "x" }, expectedVersion: 1, Cancel))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
        (await FoldersAsync(f => f.UpdateAsync(ada, team.Id, new FolderChanges { Name = "taken" }, null, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await FoldersAsync(f => f.UpdateAsync(ada, team.Id, new FolderChanges { Name = "a/b" }, null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await FoldersAsync(f => f.UpdateAsync(bob, team.Id, new FolderChanges { Name = "Mine" }, null, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
    }

    [Fact]
    public async Task Moving_a_folder_carries_its_subtree_and_checks_both_places()
    {
        var ada = await ActorAsync("ada");
        var team = await CreateAsync(ada, "Team");
        var archive = await CreateAsync(ada, "Archive");
        var prod = await CreateAsync(ada, "Prod", team.Id);
        var app = await CreateAsync(ada, "App", prod.Id);

        var moved = (await FoldersAsync(f => f.MoveAsync(ada, prod.Id, archive.Id, expectedVersion: 1, Cancel))).ShouldSucceed();

        moved.ParentId.ShouldBe(archive.Id);
        moved.Path.ShouldBe([new FolderCrumb(archive.Id, "Archive")]);
        (await FoldersAsync(f => f.GetAsync(ada, app.Id, Cancel))).ShouldSucceed().Path.Select(c => c.Name).ShouldBe(["Archive", "Prod"]);
        (await ReadAsync(db => db.Folders.SingleAsync(f => f.Id == app.Id, Cancel))).Path.ShouldStartWith($"/{archive.Id}/{prod.Id}/");
        (await FoldersAsync(f => f.MoveAsync(ada, archive.Id, app.Id, null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await FoldersAsync(f => f.MoveAsync(ada, prod.Id, archive.Id, null, Cancel))).ShouldSucceed();
        (await AuditAsync(AuditActions.FolderMoved)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Moving_needs_rights_on_the_target_and_a_free_name_there()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = await CreateAsync(ada, "Team");
        var prod = await CreateAsync(ada, "Prod", team.Id);
        var bobs = await CreateAsync(bob, "Bobs");
        var other = await CreateAsync(ada, "Other");
        await CreateAsync(ada, "Prod", other.Id);

        (await FoldersAsync(f => f.MoveAsync(ada, prod.Id, bobs.Id, null, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await FoldersAsync(f => f.MoveAsync(ada, prod.Id, other.Id, null, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await FoldersAsync(f => f.MoveAsync(ada, prod.Id, team.Id, expectedVersion: 7, Cancel))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Moving_to_the_top_level_needs_ownership_and_keeps_it()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = await CreateAsync(ada, "Team");
        var prod = await CreateAsync(ada, "Prod", team.Id);
        await GrantAsync(ada, team.Id, bob.UserId, FolderRole.Editor);

        (await FoldersAsync(f => f.MoveAsync(bob, prod.Id, null, null, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        var top = (await FoldersAsync(f => f.MoveAsync(ada, prod.Id, null, null, Cancel))).ShouldSucceed();

        top.ParentId.ShouldBeNull();
        top.MyRole.ShouldBe(FolderRole.Owner);
        (await FoldersAsync(f => f.GetAsync(bob, prod.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Moves_that_nest_too_deep_are_refused()
    {
        var ada = await ActorAsync("ada");
        Guid? parent = null;
        for (var level = 1; level <= 15; level++)
        {
            parent = (await CreateAsync(ada, $"level{level}", parent)).Id;
        }

        var branch = await CreateAsync(ada, "Branch");
        await CreateAsync(ada, "Leaf", branch.Id);

        (await FoldersAsync(f => f.MoveAsync(ada, branch.Id, parent, null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task Only_owners_delete_and_only_empty_folders()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = await CreateAsync(ada, "Team");
        var prod = await CreateAsync(ada, "Prod", team.Id);
        await GrantAsync(ada, team.Id, bob.UserId, FolderRole.Editor);

        (await FoldersAsync(f => f.DeleteAsync(ada, team.Id, null, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await FoldersAsync(f => f.DeleteAsync(bob, prod.Id, null, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await FoldersAsync(f => f.DeleteAsync(ada, prod.Id, expectedVersion: 9, Cancel))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
        (await FoldersAsync(f => f.DeleteAsync(ada, prod.Id, null, Cancel))).ShouldSucceed();
        (await FoldersAsync(f => f.DeleteAsync(ada, team.Id, null, Cancel))).ShouldSucceed();

        (await ReadAsync(db => db.Folders.CountAsync(Cancel))).ShouldBe(0);
        (await ReadAsync(db => db.FolderGrants.CountAsync(Cancel))).ShouldBe(0);
        (await AuditAsync(AuditActions.FolderDeleted)).Count.ShouldBe(2);
    }

    private async Task<FolderView> CreateAsync(Actor actor, string name, Guid? parentId = null) =>
        (await FoldersAsync(f => f.CreateAsync(actor, new NewFolder(name, parentId), Cancel))).ShouldSucceed();

    private async Task GrantAsync(Actor owner, Guid folderId, Guid userId, FolderRole role) =>
        (await FolderGrantsAsync(g => g.SetAsync(owner, folderId, userId, null, role, Cancel))).ShouldSucceed();
}

public sealed class FolderServiceTestsOnSqlite() : FolderServiceTests(DatabaseProvider.Sqlite);

public sealed class FolderServiceTestsOnPostgres() : FolderServiceTests(DatabaseProvider.Postgres);
