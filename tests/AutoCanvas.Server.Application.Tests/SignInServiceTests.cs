using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Tests;

public abstract class SignInServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private LoginThrottle? throttle;

    private LoginThrottle Throttle => throttle ??= new LoginThrottle(Clock);

    [Fact]
    public async Task A_sign_in_starts_a_session_that_validates_until_it_ends()
    {
        var ada = await CreateUserAsync("ada");

        var signedIn = (await SignInAsync("ADA", "a long enough password")).ShouldSucceed();
        var user = await SessionsAsync(s => s.ValidateAsync(signedIn.SessionToken, Cancel));

        signedIn.Me.User.Id.ShouldBe(ada.Id);
        signedIn.ExpiresAt.ShouldBe(Clock.GetUtcNow() + TimeSpan.FromDays(30));
        user.ShouldNotBeNull().UserId.ShouldBe(ada.Id);
        user.MustChangePassword.ShouldBeTrue();
        (await ReadAsync(db => db.Sessions.SingleAsync(Cancel))).TokenHash.ShouldBe(SecretTokens.Hash(signedIn.SessionToken));
        (await AuditAsync(AuditActions.SignedIn)).ShouldHaveSingleItem().ActorId.ShouldBe(ada.Id);
    }

    [Fact]
    public async Task Every_failure_is_unauthorized_and_audited()
    {
        await CreateUserAsync("ada");
        var bob = await CreateUserAsync("bob");
        await UsersAsync(users => users.UpdateAsync(bob.Id, new UserChanges { IsDisabled = true }, Cancel));

        (await SignInAsync("nobody", "a long enough password")).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await SignInAsync("ada", "wrong password!")).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await SignInAsync("bob", "a long enough password")).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await SignInAsync("ada", new string('x', 300))).ShouldFail(UseCaseErrorKind.Unauthorized);

        var failures = await AuditAsync(AuditActions.SignInFailed);
        failures.Count.ShouldBe(4);
        failures.ShouldAllBe(f => f.ActorKind == AuditActorKind.System);
        (await ReadAsync(db => db.Sessions.CountAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Five_failures_lock_the_name_until_the_lock_expires()
    {
        await CreateUserAsync("ada");
        for (var attempt = 0; attempt < LoginThrottle.MaxFailures; attempt++)
        {
            await SignInAsync("ada", "wrong password!");
        }

        (await SignInAsync("ada", "a long enough password")).ShouldFail(UseCaseErrorKind.TooManyRequests);
        Clock.Advance(LoginThrottle.LockDuration + TimeSpan.FromSeconds(1));
        (await SignInAsync("ada", "a long enough password")).ShouldSucceed();
        Throttle.IsLocked(IdentityNames.Normalize("ada")).ShouldBeFalse();
    }

    [Fact]
    public async Task Weak_hashes_are_replaced_at_sign_in()
    {
        await CreateUserAsync("ada");
        var weak = new PasswordHasher(new PasswordHashingOptions { Iterations = 10 }).Hash("a long enough password");
        await ReadAsync(async db =>
        {
            (await db.Users.SingleAsync(Cancel)).PasswordHash = weak;
            return await db.SaveChangesAsync(Cancel);
        });

        (await SignInAsync("ada", "a long enough password")).ShouldSucceed();

        (await ReadAsync(db => db.Users.SingleAsync(Cancel))).PasswordHash.ShouldStartWith("pbkdf2-sha256$1000$");
    }

    [Fact]
    public async Task Sessions_expire_when_idle_or_too_old_and_disabled_users_are_rejected()
    {
        var ada = await CreateUserAsync("ada");
        var idle = (await SignInAsync("ada", "a long enough password")).ShouldSucceed().SessionToken;
        var active = (await SignInAsync("ada", "a long enough password")).ShouldSucceed().SessionToken;

        for (var day = 0; day < 8; day++)
        {
            Clock.Advance(TimeSpan.FromDays(1));
            (await SessionsAsync(s => s.ValidateAsync(active, Cancel))).ShouldNotBeNull();
        }

        (await SessionsAsync(s => s.ValidateAsync(idle, Cancel))).ShouldBeNull();
        (await SessionsAsync(s => s.ValidateAsync("unknown", Cancel))).ShouldBeNull();
        await UsersAsync(users => users.UpdateAsync(ada.Id, new UserChanges { IsDisabled = true }, Cancel));
        (await SessionsAsync(s => s.ValidateAsync(active, Cancel))).ShouldBeNull();
    }

    [Fact]
    public async Task Sessions_are_listed_and_ended_one_by_one_or_all_at_once()
    {
        var ada = await CreateUserAsync("ada");
        var bob = await CreateUserAsync("bob");
        var firstToken = (await SignInAsync("ada", "a long enough password")).ShouldSucceed().SessionToken;
        var first = await SessionsAsync(s => s.ValidateAsync(firstToken, Cancel));
        Clock.Advance(TimeSpan.FromMinutes(5));
        await SignInAsync("ada", "a long enough password");
        await SignInAsync("bob", "a long enough password");

        var listed = await SessionsAsync(s => s.ListAsync(ada.Id, first!.SessionId, Cancel));
        listed.Count.ShouldBe(2);
        listed[1].IsCurrent.ShouldBeTrue();
        listed[0].CreatedAt.ShouldBeGreaterThan(listed[1].CreatedAt);

        (await SessionsAsync(s => s.EndAsync(bob.Id, listed[0].Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await SessionsAsync(s => s.EndAsync(ada.Id, listed[0].Id, Cancel))).ShouldSucceed();
        (await SessionsAsync(s => s.EndAllAsync(ada.Id, Cancel))).ShouldBe(1);
        (await SessionsAsync(s => s.ListAsync(ada.Id, null, Cancel))).ShouldBeEmpty();
        (await AuditAsync(AuditActions.SessionsEnded)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Changing_a_password_needs_the_current_one_and_keeps_only_the_current_session()
    {
        var ada = await CreateUserAsync("ada");
        var currentToken = (await SignInAsync("ada", "a long enough password")).ShouldSucceed().SessionToken;
        var current = await SessionsAsync(s => s.ValidateAsync(currentToken, Cancel));
        await SignInAsync("ada", "a long enough password");

        (await AccountsAsync(a => a.ChangePasswordAsync(ada.Id, current!.SessionId, "wrong", "my new long password", Cancel)))
            .ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("currentPassword");
        (await AccountsAsync(a => a.ChangePasswordAsync(ada.Id, current!.SessionId, "a long enough password", "short", Cancel)))
            .ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("newPassword");
        (await AccountsAsync(a => a.ChangePasswordAsync(ada.Id, current!.SessionId, "a long enough password", "a long enough password", Cancel)))
            .ShouldFail(UseCaseErrorKind.Validation);
        (await AccountsAsync(a => a.ChangePasswordAsync(Guid.CreateVersion7(), null, "a", "b", Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AccountsAsync(a => a.ChangePasswordAsync(ada.Id, current!.SessionId, "a long enough password", "my new long password", Cancel))).ShouldSucceed();

        (await ReadAsync(db => db.Sessions.SingleAsync(Cancel))).Id.ShouldBe(current!.SessionId!.Value);
        (await ReadAsync(db => db.Users.SingleAsync(Cancel))).MustChangePassword.ShouldBeFalse();
        (await AuditAsync(AuditActions.UserPasswordChanged)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Me_lists_the_groups_by_name()
    {
        var ada = await CreateUserAsync("ada");
        foreach (var name in new[] { "zeta", "Alpha" })
        {
            var group = (await GroupsAsync(groups => groups.CreateAsync(new NewGroup(name), Cancel))).ShouldSucceed();
            await GroupsAsync(groups => groups.AddMemberAsync(group.Id, ada.Id, Cancel));
        }

        var me = (await AccountsAsync(a => a.GetMeAsync(ada.Id, Cancel))).ShouldSucceed();

        me.Groups.Select(g => g.Name).ShouldBe(["Alpha", "zeta"]);
        (await AccountsAsync(a => a.GetMeAsync(Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    private Task<UseCaseResult<SignInResult>> SignInAsync(string userName, string password) =>
        WithContextAsync(context =>
        {
            var audit = new AuditLog(context, new SystemAuditContext(), Clock);
            var sessions = new SessionService(context, audit, Options.Create(new SessionLifetimeOptions()), Clock);
            var accounts = new AccountService(context, Hasher, audit, Clock);
            return new SignInService(context, Hasher, Throttle, sessions, accounts, audit, Clock)
                .SignInAsync(userName, password, "203.0.113.7", "tests", Cancel);
        });

    private Task<T> SessionsAsync<T>(Func<SessionService, Task<T>> action) =>
        WithContextAsync(context => action(new SessionService(
            context, new AuditLog(context, new SystemAuditContext(), Clock), Options.Create(new SessionLifetimeOptions()), Clock)));

    private Task<T> AccountsAsync<T>(Func<AccountService, Task<T>> action) =>
        WithContextAsync(context => action(new AccountService(context, Hasher, new AuditLog(context, new SystemAuditContext(), Clock), Clock)));

    private async Task<T> WithContextAsync<T>(Func<AutoCanvasDbContext, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(context);
        }
    }
}

public sealed class SignInServiceTestsOnSqlite() : SignInServiceTests(DatabaseProvider.Sqlite);

public sealed class SignInServiceTestsOnPostgres() : SignInServiceTests(DatabaseProvider.Postgres);
