using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class TokenServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    [Fact]
    public async Task A_new_token_is_shown_once_and_stored_as_a_hash()
    {
        var ada = await CreateUserAsync("ada");

        var created = (await TokensAsync(t => t.CreateAsync(ada.Id, " deploy script ", null, Cancel))).ShouldSucceed();

        created.Token.ShouldStartWith("acp_");
        created.Details.Name.ShouldBe("deploy script");
        created.Details.Hint.ShouldBe($"acp_…{created.Token[^4..]}");
        created.Details.IsExpired.ShouldBeFalse();
        var stored = await ReadAsync(db => db.ApiTokens.SingleAsync(Cancel));
        stored.TokenHash.ShouldBe(SecretTokens.Hash(created.Token));
        stored.TokenHash.ShouldNotContain(created.Token[4..]);
        var audit = (await AuditAsync(AuditActions.TokenCreated)).ShouldHaveSingleItem();
        audit.Details!.ShouldNotContain(created.Token);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("tab\tname", false)]
    [InlineData("ok", true)]
    public async Task Token_names_are_checked(string name, bool valid)
    {
        var ada = await CreateUserAsync("ada");

        var result = await TokensAsync(t => t.CreateAsync(ada.Id, name, null, Cancel));

        (result.Error is null).ShouldBe(valid);
    }

    [Fact]
    public async Task Tokens_need_an_owner_and_a_future_expiry()
    {
        var ada = await CreateUserAsync("ada");

        (await TokensAsync(t => t.CreateAsync(ada.Id, "ci", Clock.GetUtcNow(), Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("expiresAt");
        (await TokensAsync(t => t.CreateAsync(Guid.CreateVersion7(), "ci", null, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await TokensAsync(t => t.CreateAsync(ada.Id, new string('n', 101), null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task Tokens_identify_their_owner_until_they_expire()
    {
        var ada = await CreateUserAsync("ada");
        var forever = (await TokensAsync(t => t.CreateAsync(ada.Id, "forever", null, Cancel))).ShouldSucceed().Token;
        Clock.Advance(TimeSpan.FromSeconds(1));
        var shortLived = (await TokensAsync(t => t.CreateAsync(ada.Id, "short", Clock.GetUtcNow().AddHours(1), Cancel))).ShouldSucceed().Token;

        var owner = await TokensAsync(t => t.ValidateAsync(shortLived, Cancel));
        Clock.Advance(TimeSpan.FromHours(2));

        owner.ShouldNotBeNull().UserId.ShouldBe(ada.Id);
        owner.SessionId.ShouldBeNull();
        (await TokensAsync(t => t.ValidateAsync(shortLived, Cancel))).ShouldBeNull();
        (await TokensAsync(t => t.ValidateAsync(forever, Cancel))).ShouldNotBeNull();
        (await TokensAsync(t => t.ValidateAsync("acp_unknown", Cancel))).ShouldBeNull();
        (await TokensAsync(t => t.ValidateAsync("not a token", Cancel))).ShouldBeNull();

        var listed = (await TokensAsync(t => t.ListAsync(ada.Id, Cancel))).ShouldSucceed();
        listed.Select(t => t.Name).ShouldBe(["short", "forever"]);
        listed[0].IsExpired.ShouldBeTrue();
        listed[1].LastUsedAt.ShouldBe(Clock.GetUtcNow());
    }

    [Fact]
    public async Task Tokens_of_disabled_users_stop_working()
    {
        var ada = await CreateUserAsync("ada");
        var token = (await TokensAsync(t => t.CreateAsync(ada.Id, "ci", null, Cancel))).ShouldSucceed().Token;

        await UsersAsync(users => users.UpdateAsync(ada.Id, new UserChanges { IsDisabled = true }, Cancel));

        (await TokensAsync(t => t.ValidateAsync(token, Cancel))).ShouldBeNull();
    }

    [Fact]
    public async Task Only_the_owner_deletes_a_token()
    {
        var ada = await CreateUserAsync("ada");
        var bob = await CreateUserAsync("bob");
        var created = (await TokensAsync(t => t.CreateAsync(ada.Id, "ci", null, Cancel))).ShouldSucceed();

        (await TokensAsync(t => t.DeleteAsync(bob.Id, created.Details.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await TokensAsync(t => t.DeleteAsync(ada.Id, created.Details.Id, Cancel))).ShouldSucceed();

        (await TokensAsync(t => t.ValidateAsync(created.Token, Cancel))).ShouldBeNull();
        (await AuditAsync(AuditActions.TokenDeleted)).ShouldHaveSingleItem();
        (await TokensAsync(t => t.ListAsync(Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    private async Task<T> TokensAsync<T>(Func<TokenService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new TokenService(context, new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }
}

public sealed class TokenServiceTestsOnSqlite() : TokenServiceTests(DatabaseProvider.Sqlite);

public sealed class TokenServiceTestsOnPostgres() : TokenServiceTests(DatabaseProvider.Postgres);
