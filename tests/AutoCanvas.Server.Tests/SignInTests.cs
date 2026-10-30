using System.Net;
using System.Net.Http.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Tests;

public abstract class SignInTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private static Uri Me => new("/api/v1/me", UriKind.Relative);

    [Fact]
    public async Task Signing_in_sets_a_protected_session_cookie()
    {
        await using var factory = Factory();
        await factory.CreateUserAsync("Ada");
        var client = factory.CreateApiClient();

        var response = await client.SignInAsync("ada");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("autocanvas_session=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/", Case.Insensitive);
        cookie.ShouldNotContain("secure", Case.Insensitive);
        var body = await response.JsonAsync();
        body.GetProperty("me").GetProperty("user").GetProperty("userName").GetString().ShouldBe("Ada");
        body.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(factory.Clock.GetUtcNow().AddDays(30));
        body.ToString().ShouldNotContain(cookie.Split(';')[0].Split('=')[1]);

        var me = await (await client.GetAsync(Me, Cancel)).JsonAsync();
        me.GetProperty("user").GetProperty("userName").GetString().ShouldBe("Ada");
    }

    [Fact]
    public async Task The_cookie_is_secure_when_the_public_url_is_https()
    {
        await using var factory = Factory().With("Server:PublicUrl", "https://autocanvas.example.org");
        await factory.CreateUserAsync("ada");

        var response = await factory.CreateApiClient().SignInAsync("ada");

        response.Headers.GetValues("Set-Cookie").Single().ShouldContain("secure", Case.Insensitive);
    }

    [Fact]
    public async Task Every_failed_sign_in_gives_the_same_answer()
    {
        await using var factory = Factory();
        await factory.CreateUserAsync("ada");
        var disabled = await factory.CreateUserAsync("bob");
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        (await admin.PatchAsJsonAsync($"/api/v1/users/{disabled.Id}", new { isDisabled = true }, Cancel)).EnsureSuccessStatusCode();
        var client = factory.CreateApiClient();

        var answers = new List<string>();
        foreach (var (name, password) in new[] { ("nobody", ApiClient.Password), ("ada", "wrong password!"), ("bob", ApiClient.Password) })
        {
            var response = await client.SignInAsync(name, password);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            answers.Add((await response.JsonAsync()).GetProperty("detail").GetString()!);
        }

        answers.Distinct(StringComparer.Ordinal).ShouldHaveSingleItem();
        var reasons = await ReadAsync(db => db.AuditEvents.Where(e => e.Action == "auth.sign_in_failed").Select(e => e.Details!).ToListAsync(Cancel));
        reasons.Count.ShouldBe(3);
        reasons.ShouldContain(d => d.Contains("unknown user", StringComparison.Ordinal));
        reasons.ShouldContain(d => d.Contains("wrong password", StringComparison.Ordinal));
        reasons.ShouldContain(d => d.Contains("disabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Requests_without_a_valid_session_are_unauthorized()
    {
        await using var factory = Factory();
        var client = factory.CreateApiClient();

        var anonymous = await client.GetAsync(Me, Cancel);
        client.DefaultRequestHeaders.Add("Cookie", "autocanvas_session=forged");
        var forged = await client.GetAsync(Me, Cancel);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.ProblemTypeAsync()).ShouldBe("unauthorized");
        forged.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Changes_without_the_request_header_are_refused()
    {
        await using var factory = Factory();
        await factory.CreateUserAsync("ada");
        var client = factory.CreateClient();

        var response = await client.SignInAsync("ada");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("X-AutoCanvas-Request: 1");
        (await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_password_set_by_someone_else_must_be_changed_first()
    {
        await using var factory = Factory();
        var client = await factory.SignedInAsync("root", isAdmin: true, mustChangePassword: true);

        var blocked = await client.GetAsync(new Uri("/api/v1/users", UriKind.Relative), Cancel);
        var me = await (await client.GetAsync(Me, Cancel)).JsonAsync();
        var wrong = await client.PutAsJsonAsync("/api/v1/me/password", new { currentPassword = "not the password", newPassword = "my own new password" }, Cancel);
        var same = await client.PutAsJsonAsync("/api/v1/me/password", new { currentPassword = ApiClient.Password, newPassword = ApiClient.Password }, Cancel);
        var changed = await client.PutAsJsonAsync("/api/v1/me/password", new { currentPassword = ApiClient.Password, newPassword = "my own new password" }, Cancel);
        var allowed = await client.GetAsync(new Uri("/api/v1/users", UriKind.Relative), Cancel);

        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await blocked.ProblemTypeAsync()).ShouldBe("password-change-required");
        me.GetProperty("user").GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrong.JsonAsync()).GetProperty("field").GetString().ShouldBe("currentPassword");
        same.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_the_password_ends_the_other_sessions()
    {
        await using var factory = Factory();
        var first = await factory.SignedInAsync("ada");
        var second = factory.CreateApiClient();
        (await second.SignInAsync("ada")).EnsureSuccessStatusCode();

        (await first.PutAsJsonAsync("/api/v1/me/password", new { currentPassword = ApiClient.Password, newPassword = "my own new password" }, Cancel))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await first.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await factory.CreateApiClient().SignInAsync("ada", "my own new password")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_for_good()
    {
        await using var factory = Factory();
        await factory.CreateUserAsync("ada");
        var client = factory.CreateApiClient();
        var signIn = await client.SignInAsync("ada");
        var token = signIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        var signOut = await client.PostAsync(new Uri("/api/v1/auth/sign-out", UriKind.Relative), null, Cancel);
        var replay = factory.CreateClient();
        replay.DefaultRequestHeaders.Add("Cookie", token);

        signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        signOut.Headers.GetValues("Set-Cookie").Single().ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
        (await replay.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadAsync(db => db.AuditEvents.CountAsync(e => e.Action == "auth.signed_out", Cancel))).ShouldBe(1);
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_every_session()
    {
        await using var factory = Factory();
        var first = await factory.SignedInAsync("ada");
        var second = factory.CreateApiClient();
        (await second.SignInAsync("ada")).EnsureSuccessStatusCode();

        (await first.PostAsync(new Uri("/api/v1/auth/sign-out-everywhere", UriKind.Relative), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await first.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await second.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sessions_end_after_a_week_without_requests()
    {
        await using var factory = Factory();
        var client = await factory.SignedInAsync("ada");

        factory.Clock.Advance(TimeSpan.FromDays(6));
        (await client.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Clock.Advance(TimeSpan.FromDays(7));

        (await client.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadAsync(db => db.Sessions.CountAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Sessions_end_after_thirty_days_however_active()
    {
        await using var factory = Factory();
        var client = await factory.SignedInAsync("ada");

        for (var day = 0; day < 29; day++)
        {
            factory.Clock.Advance(TimeSpan.FromDays(1));
            (await client.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        factory.Clock.Advance(TimeSpan.FromDays(1));
        (await client.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Users_see_and_end_their_own_sessions()
    {
        await using var factory = Factory();
        var first = await factory.SignedInAsync("ada");
        var second = factory.CreateApiClient();
        (await second.SignInAsync("ada")).EnsureSuccessStatusCode();
        var other = await factory.SignedInAsync("bob");

        var sessions = (await (await first.GetAsync(new Uri("/api/v1/me/sessions", UriKind.Relative), Cancel)).JsonAsync()).EnumerateArray().ToList();
        sessions.Count.ShouldBe(2);
        sessions.Count(s => s.GetProperty("isCurrent").GetBoolean()).ShouldBe(1);
        sessions.ShouldAllBe(s => s.GetProperty("userAgent").GetString() == "autocanvas-tests/1.0");
        var otherSession = sessions.Single(s => !s.GetProperty("isCurrent").GetBoolean()).GetProperty("id").GetGuid();

        (await other.DeleteAsync(new Uri($"/api/v1/me/sessions/{otherSession}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await first.DeleteAsync(new Uri($"/api/v1/me/sessions/{otherSession}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await second.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await first.GetAsync(Me, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_name_for_a_while()
    {
        await using var factory = Factory();
        await factory.CreateUserAsync("ada");
        var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await client.SignInAsync("ada", "wrong password!")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var locked = await client.SignInAsync("ada");
        factory.Clock.Advance(TimeSpan.FromMinutes(16));
        var unlocked = await client.SignInAsync("ada");

        locked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await locked.ProblemTypeAsync()).ShouldBe("too-many-requests");
        unlocked.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Too_many_attempts_from_one_address_are_refused()
    {
        await using var factory = Factory();
        var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            (await client.SignInAsync($"nobody{attempt}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var refused = await client.SignInAsync("nobody");

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.ShouldNotBeNull();
        (await refused.ProblemTypeAsync()).ShouldBe("too-many-requests");
    }

    [Fact]
    public async Task Incomplete_requests_are_bad_requests()
    {
        await using var factory = Factory();

        var response = await factory.CreateApiClient().PostAsJsonAsync("/api/v1/auth/sign-in", new { userName = "ada" }, Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sign_ins_are_recorded_with_the_user_as_actor()
    {
        await using var factory = Factory();
        var ada = await factory.CreateUserAsync("ada");

        (await factory.CreateApiClient().SignInAsync("ada")).EnsureSuccessStatusCode();

        var signedIn = await ReadAsync(db => db.AuditEvents.SingleAsync(e => e.Action == "auth.signed_in", Cancel));
        signedIn.ActorKind.ShouldBe(AuditActorKind.User);
        signedIn.ActorId.ShouldBe(ada.Id);
        signedIn.UserAgent.ShouldBe("autocanvas-tests/1.0");
        (await ReadAsync(db => db.Users.SingleAsync(Cancel))).LastSignInAt.ShouldBe(factory.Clock.GetUtcNow());
    }

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);

    private async Task<T> ReadAsync<T>(Func<AutoCanvasDbContext, Task<T>> query)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await query(context);
        }
    }
}

public sealed class SignInTestsOnSqlite() : SignInTests(DatabaseProvider.Sqlite);

public sealed class SignInTestsOnPostgres() : SignInTests(DatabaseProvider.Postgres);
