using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class UserServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    [Fact]
    public async Task Creating_a_user_stores_a_hash_and_records_it()
    {
        var created = (await UsersAsync(users => users.CreateAsync(
            new NewUser("Ada", "a long enough password") { DisplayName = " Ada Lovelace ", Email = " ada@example.org ", IsAdmin = true },
            Cancel))).ShouldSucceed();

        created.UserName.ShouldBe("Ada");
        created.DisplayName.ShouldBe("Ada Lovelace");
        created.Email.ShouldBe("ada@example.org");
        created.IsAdmin.ShouldBeTrue();
        created.MustChangePassword.ShouldBeTrue();
        created.CreatedAt.ShouldBe(Clock.GetUtcNow());

        var stored = await ReadAsync(db => db.Users.SingleAsync(Cancel));
        stored.NormalizedUserName.ShouldBe("ADA");
        stored.PasswordHash.ShouldNotContain("a long enough password");
        Hasher.Verify("a long enough password", stored.PasswordHash).ShouldBe(PasswordVerification.Succeeded);

        var audit = (await AuditAsync(AuditActions.UserCreated)).ShouldHaveSingleItem();
        audit.TargetType.ShouldBe("user");
        audit.TargetId.ShouldBe(created.Id.ToString());
        audit.Details.ShouldNotBeNull().ShouldNotContain("password");
    }

    [Fact]
    public async Task The_display_name_defaults_to_the_user_name() =>
        (await CreateUserAsync("grace")).DisplayName.ShouldBe("grace");

    [Fact]
    public async Task User_names_are_unique_regardless_of_case()
    {
        await CreateUserAsync("ada");

        var error = (await UsersAsync(users => users.CreateAsync(new NewUser("ADA", "a long enough password"), Cancel)))
            .ShouldFail(UseCaseErrorKind.Conflict);

        error.Field.ShouldBe("userName");
    }

    [Theory]
    [InlineData("ada lovelace", "a long enough password", null, null, "userName")]
    [InlineData("ada", "short", null, null, "password")]
    [InlineData("ada", "a long enough password", "not an address", null, "email")]
    [InlineData("ada", "a long enough password", null, "   ", "displayName")]
    public async Task Invalid_accounts_are_refused(string userName, string password, string? email, string? displayName, string field)
    {
        var error = (await UsersAsync(users => users.CreateAsync(new NewUser(userName, password) { Email = email, DisplayName = displayName }, Cancel)))
            .ShouldFail(UseCaseErrorKind.Validation);

        error.Field.ShouldBe(field);
        (await ReadAsync(db => db.Users.CountAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Users_are_found_by_id_and_by_name_regardless_of_case()
    {
        var ada = await CreateUserAsync("Ada");

        (await UsersAsync(users => users.GetAsync(ada.Id, Cancel))).ShouldSucceed().UserName.ShouldBe("Ada");
        (await UsersAsync(users => users.FindByUserNameAsync("aDA", Cancel))).ShouldSucceed().Id.ShouldBe(ada.Id);
        (await UsersAsync(users => users.GetAsync(Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await UsersAsync(users => users.FindByUserNameAsync("grace", Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Lists_page_through_users_in_name_order()
    {
        foreach (var name in new[] { "dora", "Bob", "eve", "alice", "carl" })
        {
            await CreateUserAsync(name);
        }

        var names = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = (await UsersAsync(users => users.ListAsync(new PageRequest(2, cursor), Cancel))).ShouldSucceed();
            names.AddRange(page.Items.Select(u => u.UserName));
            cursor = page.Next;
            pages++;
        }
        while (cursor is not null);

        names.ShouldBe(["alice", "Bob", "carl", "dora", "eve"]);
        pages.ShouldBe(3);
    }

    [Theory]
    [InlineData(0, null, "limit")]
    [InlineData(201, null, "limit")]
    [InlineData(10, "%%%", "cursor")]
    public async Task Impossible_pages_are_refused(int limit, string? cursor, string field) =>
        (await UsersAsync(users => users.ListAsync(new PageRequest(limit, cursor), Cancel)))
            .ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe(field);

    [Fact]
    public async Task Updates_change_only_what_is_given_and_record_the_fields()
    {
        var ada = await CreateUserAsync("ada");

        var updated = (await UsersAsync(users => users.UpdateAsync(ada.Id, new UserChanges { DisplayName = "Ada L.", Email = "ada@example.org" }, Cancel)))
            .ShouldSucceed();
        Clock.Advance(TimeSpan.FromSeconds(1));
        var cleared = (await UsersAsync(users => users.UpdateAsync(ada.Id, new UserChanges { Email = "" }, Cancel))).ShouldSucceed();
        await UsersAsync(users => users.UpdateAsync(ada.Id, new UserChanges { DisplayName = "Ada L." }, Cancel));

        updated.DisplayName.ShouldBe("Ada L.");
        updated.Email.ShouldBe("ada@example.org");
        cleared.Email.ShouldBeNull();
        cleared.DisplayName.ShouldBe("Ada L.");

        var audits = await AuditAsync(AuditActions.UserUpdated);
        audits.Count.ShouldBe(2);
        JsonDocument.Parse(audits.OrderBy(a => a.OccurredAt).First().Details!).RootElement.GetProperty("changed").EnumerateArray()
            .Select(f => f.GetString()).ShouldBe(["displayName", "email"]);
    }

    [Fact]
    public async Task Updating_an_unknown_user_or_with_invalid_values_fails()
    {
        var ada = await CreateUserAsync("ada");

        (await UsersAsync(users => users.UpdateAsync(Guid.CreateVersion7(), new UserChanges(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await UsersAsync(users => users.UpdateAsync(ada.Id, new UserChanges { Email = "nope" }, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task The_last_enabled_admin_cannot_lose_admin_rights()
    {
        var admin = await CreateUserAsync("root", isAdmin: true);

        (await UsersAsync(users => users.UpdateAsync(admin.Id, new UserChanges { IsAdmin = false }, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await UsersAsync(users => users.UpdateAsync(admin.Id, new UserChanges { IsDisabled = true }, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await UsersAsync(users => users.DeleteAsync(admin.Id, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);

        var second = await CreateUserAsync("ops", isAdmin: true);
        (await UsersAsync(users => users.UpdateAsync(admin.Id, new UserChanges { IsAdmin = false }, Cancel))).ShouldSucceed().IsAdmin.ShouldBeFalse();
        (await UsersAsync(users => users.DeleteAsync(second.Id, Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
    }

    [Fact]
    public async Task Disabling_a_user_ends_their_sessions()
    {
        var ada = await CreateUserAsync("ada");
        await AddSessionAsync(ada.Id);

        (await UsersAsync(users => users.UpdateAsync(ada.Id, new UserChanges { IsDisabled = true }, Cancel))).ShouldSucceed().IsDisabled.ShouldBeTrue();

        (await ReadAsync(db => db.Sessions.CountAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_user_removes_sessions_and_memberships_but_keeps_the_audit_trail()
    {
        var ada = await CreateUserAsync("ada");
        await AddSessionAsync(ada.Id);
        var group = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup("ops"), Cancel))).ShouldSucceed();
        await GroupsAsync(groups => groups.AddMemberAsync(group.Id, ada.Id, Cancel));

        (await UsersAsync(users => users.DeleteAsync(ada.Id, Cancel))).ShouldSucceed();

        (await ReadAsync(db => db.Users.CountAsync(Cancel))).ShouldBe(0);
        (await ReadAsync(db => db.Sessions.CountAsync(Cancel))).ShouldBe(0);
        (await ReadAsync(db => db.GroupMembers.CountAsync(Cancel))).ShouldBe(0);
        (await AuditAsync(AuditActions.UserDeleted)).ShouldHaveSingleItem().TargetId.ShouldBe(ada.Id.ToString());
        (await UsersAsync(users => users.DeleteAsync(ada.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Resetting_a_password_forces_a_change_and_ends_sessions()
    {
        var ada = (await UsersAsync(users => users.CreateAsync(new NewUser("ada", "a long enough password") { MustChangePassword = false }, Cancel))).ShouldSucceed();
        await AddSessionAsync(ada.Id);
        Clock.Advance(TimeSpan.FromHours(1));

        var reset = (await UsersAsync(users => users.ResetPasswordAsync(ada.Id, "another long password", Cancel))).ShouldSucceed();

        reset.MustChangePassword.ShouldBeTrue();
        var stored = await ReadAsync(db => db.Users.SingleAsync(Cancel));
        Hasher.Verify("another long password", stored.PasswordHash).ShouldBe(PasswordVerification.Succeeded);
        stored.PasswordChangedAt.ShouldBe(Clock.GetUtcNow());
        (await ReadAsync(db => db.Sessions.CountAsync(Cancel))).ShouldBe(0);
        (await AuditAsync(AuditActions.UserPasswordReset)).ShouldHaveSingleItem();
        (await UsersAsync(users => users.ResetPasswordAsync(ada.Id, "short", Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await UsersAsync(users => users.ResetPasswordAsync(Guid.CreateVersion7(), "another long password", Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task The_first_admin_is_created_only_while_there_are_no_accounts()
    {
        var first = await UsersAsync(users => users.CreateFirstAdminAsync("root", "a long enough password", Cancel));
        var second = await UsersAsync(users => users.CreateFirstAdminAsync("other", "a long enough password", Cancel));

        first.ShouldNotBeNull().ShouldSucceed().IsAdmin.ShouldBeTrue();
        first.Value!.MustChangePassword.ShouldBeTrue();
        second.ShouldBeNull();
        (await ReadAsync(db => db.Users.CountAsync(Cancel))).ShouldBe(1);
    }
}

public sealed class UserServiceTestsOnSqlite() : UserServiceTests(DatabaseProvider.Sqlite);

public sealed class UserServiceTestsOnPostgres() : UserServiceTests(DatabaseProvider.Postgres);
