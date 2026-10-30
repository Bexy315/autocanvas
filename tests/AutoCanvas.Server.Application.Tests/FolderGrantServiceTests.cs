using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class FolderGrantServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    [Fact]
    public async Task Owners_grant_change_list_and_remove_roles()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var ops = (await GroupsAsync(g => g.CreateAsync(new NewGroup("ops"), Cancel))).ShouldSucceed();
        var team = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("Team"), Cancel))).ShouldSucceed();

        (await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, null, ops.Id, FolderRole.Viewer, Cancel))).ShouldSucceed().SubjectName.ShouldBe("ops");
        Clock.Advance(TimeSpan.FromSeconds(1));
        (await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, bob.UserId, null, FolderRole.Viewer, Cancel))).ShouldSucceed();
        Clock.Advance(TimeSpan.FromSeconds(1));
        var changed = (await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, bob.UserId, null, FolderRole.Editor, Cancel))).ShouldSucceed();

        changed.Role.ShouldBe(FolderRole.Editor);
        var listed = (await FolderGrantsAsync(g => g.ListAsync(ada, team.Id, Cancel))).ShouldSucceed();
        listed.Select(g => (g.SubjectType, g.SubjectName, g.Role)).ShouldBe(
            [("group", "ops", FolderRole.Viewer), ("user", "ada", FolderRole.Owner), ("user", "bob", FolderRole.Editor)]);
        var lastSet = (await AuditAsync(AuditActions.FolderGrantSet)).OrderBy(a => a.OccurredAt).Last();
        JsonDocument.Parse(lastSet.Details!).RootElement.GetProperty("previousRole").GetString().ShouldBe("Viewer");

        (await FolderGrantsAsync(g => g.RemoveAsync(ada, team.Id, changed.Id, Cancel))).ShouldSucceed();
        (await FolderGrantsAsync(g => g.RemoveAsync(ada, team.Id, changed.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AuditAsync(AuditActions.FolderGrantRemoved)).ShouldHaveSingleItem().FolderId.ShouldBe(team.Id);
    }

    [Fact]
    public async Task Setting_the_same_role_again_changes_nothing()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("Team"), Cancel))).ShouldSucceed();

        await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, bob.UserId, null, FolderRole.Viewer, Cancel));
        await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, bob.UserId, null, FolderRole.Viewer, Cancel));

        (await AuditAsync(AuditActions.FolderGrantSet)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Group_grants_reach_the_members_and_go_with_the_group()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var ops = (await GroupsAsync(g => g.CreateAsync(new NewGroup("ops"), Cancel))).ShouldSucceed();
        await GroupsAsync(g => g.AddMemberAsync(ops.Id, bob.UserId, Cancel));
        var team = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("Team"), Cancel))).ShouldSucceed();
        await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, null, ops.Id, FolderRole.Operator, Cancel));

        (await FoldersAsync(f => f.GetAsync(bob, team.Id, Cancel))).ShouldSucceed().MyRole.ShouldBe(FolderRole.Operator);

        await GroupsAsync(g => g.DeleteAsync(ops.Id, Cancel));

        (await FoldersAsync(f => f.GetAsync(bob, team.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(db => db.FolderGrants.CountAsync(g => g.GroupId != null, Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Only_owners_manage_grants_and_only_for_existing_subjects()
    {
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("Team"), Cancel))).ShouldSucceed();
        await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, bob.UserId, null, FolderRole.Editor, Cancel));

        (await FolderGrantsAsync(g => g.ListAsync(bob, team.Id, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await FolderGrantsAsync(g => g.SetAsync(bob, team.Id, bob.UserId, null, FolderRole.Owner, Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await FolderGrantsAsync(g => g.RemoveAsync(bob, team.Id, Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, null, null, FolderRole.Viewer, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, bob.UserId, Guid.CreateVersion7(), FolderRole.Viewer, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, bob.UserId, null, FolderRole.None, Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("role");
        (await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, Guid.CreateVersion7(), null, FolderRole.Viewer, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await FolderGrantsAsync(g => g.SetAsync(ada, team.Id, null, Guid.CreateVersion7(), FolderRole.Viewer, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Admins_manage_grants_everywhere()
    {
        var ada = await ActorAsync("ada");
        var admin = await ActorAsync("root", isAdmin: true);
        var team = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("Team"), Cancel))).ShouldSucceed();
        await FoldersAsync(f => f.SetRestrictedAsync(ada, team.Id, true, null, Cancel));

        (await FolderGrantsAsync(g => g.ListAsync(admin, team.Id, Cancel))).ShouldSucceed().ShouldHaveSingleItem();
        (await FolderGrantsAsync(g => g.SetAsync(admin, team.Id, admin.UserId, null, FolderRole.Viewer, Cancel))).ShouldSucceed();
    }
}

public sealed class FolderGrantServiceTestsOnSqlite() : FolderGrantServiceTests(DatabaseProvider.Sqlite);

public sealed class FolderGrantServiceTestsOnPostgres() : FolderGrantServiceTests(DatabaseProvider.Postgres);
