using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class GroupServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    [Fact]
    public async Task Groups_are_created_with_unique_names()
    {
        var created = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup(" Release managers ", " Ship it "), Cancel))).ShouldSucceed();

        created.Name.ShouldBe("Release managers");
        created.Description.ShouldBe("Ship it");
        created.Members.ShouldBeEmpty();
        (await GroupsAsync(groups => groups.CreateAsync(new NewGroup("RELEASE MANAGERS"), Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await GroupsAsync(groups => groups.CreateAsync(new NewGroup(""), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await GroupsAsync(groups => groups.CreateAsync(new NewGroup("ops", new string('d', 501)), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await AuditAsync(AuditActions.GroupCreated)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Members_are_added_once_and_listed_by_user_name()
    {
        var group = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup("ops"), Cancel))).ShouldSucceed();
        var zoe = await CreateUserAsync("zoe");
        var ada = await CreateUserAsync("ada");

        await GroupsAsync(groups => groups.AddMemberAsync(group.Id, zoe.Id, Cancel));
        await GroupsAsync(groups => groups.AddMemberAsync(group.Id, ada.Id, Cancel));
        (await GroupsAsync(groups => groups.AddMemberAsync(group.Id, ada.Id, Cancel))).ShouldSucceed();

        var details = (await GroupsAsync(groups => groups.GetAsync(group.Id, Cancel))).ShouldSucceed();
        details.Members.Select(m => m.UserName).ShouldBe(["ada", "zoe"]);
        (await AuditAsync(AuditActions.GroupMemberAdded)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Members_are_removed_once()
    {
        var group = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup("ops"), Cancel))).ShouldSucceed();
        var ada = await CreateUserAsync("ada");
        await GroupsAsync(groups => groups.AddMemberAsync(group.Id, ada.Id, Cancel));

        (await GroupsAsync(groups => groups.RemoveMemberAsync(group.Id, ada.Id, Cancel))).ShouldSucceed();
        (await GroupsAsync(groups => groups.RemoveMemberAsync(group.Id, ada.Id, Cancel))).ShouldSucceed();

        (await ReadAsync(db => db.GroupMembers.CountAsync(Cancel))).ShouldBe(0);
        (await AuditAsync(AuditActions.GroupMemberRemoved)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Membership_changes_need_an_existing_group_and_user()
    {
        var group = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup("ops"), Cancel))).ShouldSucceed();
        var ada = await CreateUserAsync("ada");

        (await GroupsAsync(groups => groups.AddMemberAsync(Guid.CreateVersion7(), ada.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await GroupsAsync(groups => groups.AddMemberAsync(group.Id, Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await GroupsAsync(groups => groups.RemoveMemberAsync(group.Id, Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Groups_are_renamed_and_described()
    {
        var ops = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup("ops", "old"), Cancel))).ShouldSucceed();
        await GroupsAsync(groups => groups.CreateAsync(new NewGroup("dev"), Cancel));

        var renamed = (await GroupsAsync(groups => groups.UpdateAsync(ops.Id, new GroupChanges { Name = "Operations", Description = "" }, Cancel))).ShouldSucceed();
        await GroupsAsync(groups => groups.UpdateAsync(ops.Id, new GroupChanges { Name = "Operations" }, Cancel));

        renamed.Name.ShouldBe("Operations");
        renamed.Description.ShouldBeNull();
        (await AuditAsync(AuditActions.GroupUpdated)).ShouldHaveSingleItem();
        (await GroupsAsync(groups => groups.UpdateAsync(ops.Id, new GroupChanges { Name = "DEV" }, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await GroupsAsync(groups => groups.UpdateAsync(ops.Id, new GroupChanges { Name = " " }, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await GroupsAsync(groups => groups.UpdateAsync(Guid.CreateVersion7(), new GroupChanges(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Deleting_a_group_keeps_its_members()
    {
        var group = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup("ops"), Cancel))).ShouldSucceed();
        var ada = await CreateUserAsync("ada");
        await GroupsAsync(groups => groups.AddMemberAsync(group.Id, ada.Id, Cancel));

        (await GroupsAsync(groups => groups.DeleteAsync(group.Id, Cancel))).ShouldSucceed();

        (await ReadAsync(db => db.Groups.CountAsync(Cancel))).ShouldBe(0);
        (await ReadAsync(db => db.Users.CountAsync(Cancel))).ShouldBe(1);
        (await AuditAsync(AuditActions.GroupDeleted)).ShouldHaveSingleItem();
        (await GroupsAsync(groups => groups.DeleteAsync(group.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await GroupsAsync(groups => groups.GetAsync(group.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Lists_page_through_groups_with_member_counts()
    {
        var ada = await CreateUserAsync("ada");
        foreach (var name in new[] { "gamma", "Alpha", "beta" })
        {
            var group = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup(name), Cancel))).ShouldSucceed();
            await GroupsAsync(groups => groups.AddMemberAsync(group.Id, ada.Id, Cancel));
        }

        var first = (await GroupsAsync(groups => groups.ListAsync(new PageRequest(2), Cancel))).ShouldSucceed();
        var second = (await GroupsAsync(groups => groups.ListAsync(new PageRequest(2, first.Next), Cancel))).ShouldSucceed();

        first.Items.Select(g => g.Name).ShouldBe(["Alpha", "beta"]);
        first.Items.ShouldAllBe(g => g.MemberCount == 1);
        second.Items.Select(g => g.Name).ShouldBe(["gamma"]);
        second.Next.ShouldBeNull();
        (await GroupsAsync(groups => groups.ListAsync(new PageRequest(500), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }
}

public sealed class GroupServiceTestsOnSqlite() : GroupServiceTests(DatabaseProvider.Sqlite);

public sealed class GroupServiceTestsOnPostgres() : GroupServiceTests(DatabaseProvider.Postgres);
