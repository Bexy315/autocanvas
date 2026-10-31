using System.Net;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Tests;

public abstract class AdminCommandTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private static readonly PasswordHasher Hasher = new(new PasswordHashingOptions());

    [Fact]
    public async Task Creating_an_admin_prints_a_one_time_password()
    {
        var (exitCode, output, _) = await RunAsync("admin", "create", "root", "--display-name", "Root");

        exitCode.ShouldBe(0);
        output.ShouldContain("Created admin 'root'.");
        var password = output.Split('\n').Single(l => l.StartsWith("One-time password: ", StringComparison.Ordinal))["One-time password: ".Length..].Trim();

        var user = await ReadUserAsync();
        user.IsAdmin.ShouldBeTrue();
        user.DisplayName.ShouldBe("Root");
        user.MustChangePassword.ShouldBeTrue();
        Hasher.Verify(password, user.PasswordHash).ShouldBe(PasswordVerification.Succeeded);
    }

    [Fact]
    public async Task An_admin_can_be_created_with_a_typed_password()
    {
        var (exitCode, output, _) = await RunWithInputAsync("a long enough password\n", "admin", "create", "root", "--password-stdin");

        exitCode.ShouldBe(0);
        output.ShouldNotContain("One-time password");
        var user = await ReadUserAsync();
        user.MustChangePassword.ShouldBeFalse();
        Hasher.Verify("a long enough password", user.PasswordHash).ShouldBe(PasswordVerification.Succeeded);
    }

    [Fact]
    public async Task Invalid_admins_are_refused_with_the_reason()
    {
        await RunAsync("admin", "create", "root");

        var (taken, _, takenError) = await RunAsync("admin", "create", "ROOT");
        var (weak, _, weakError) = await RunWithInputAsync("short\n", "admin", "create", "other", "--password-stdin");

        taken.ShouldBe(1);
        takenError.ShouldContain("is taken");
        weak.ShouldBe(1);
        weakError.ShouldContain("at least 12 characters");
    }

    [Fact]
    public async Task Resetting_a_password_prints_a_new_one_time_password()
    {
        await RunWithInputAsync("a long enough password\n", "admin", "create", "root", "--password-stdin");

        var (exitCode, output, _) = await RunAsync("admin", "reset-password", "Root");
        var (unknown, _, unknownError) = await RunAsync("admin", "reset-password", "nobody");

        exitCode.ShouldBe(0);
        output.ShouldContain("Reset the password of 'root'.");
        (await ReadUserAsync()).MustChangePassword.ShouldBeTrue();
        unknown.ShouldBe(1);
        unknownError.ShouldContain("There is no user 'nobody'.");
    }

    [Fact]
    public async Task The_server_creates_the_bootstrap_admin_while_there_are_no_accounts()
    {
        await using (var factory = BootstrapFactory("root", "a long enough password"))
        {
            (await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await using (var again = BootstrapFactory("other", "a long enough password"))
        {
            _ = again.CreateClient();
        }

        var user = await ReadUserAsync();
        user.UserName.ShouldBe("root");
        user.IsAdmin.ShouldBeTrue();
        user.MustChangePassword.ShouldBeTrue();
    }

    [Fact]
    public async Task A_bootstrap_admin_that_cannot_be_created_stops_the_server()
    {
        var message = await ServerStart.FailsAsync<InvalidOperationException>(Database, ("Bootstrap:AdminUserName", "root"), ("Bootstrap:AdminPassword", "short"));

        message.ShouldContain("at least 12 characters");
    }

    [Fact]
    public async Task Bootstrap_settings_need_both_name_and_password()
    {
        (await ServerStart.FailsAsync<Microsoft.Extensions.Options.OptionsValidationException>(Database, ("Bootstrap:AdminUserName", "root")))
            .ShouldContain("must be set together");
    }

    private AutoCanvasFactory BootstrapFactory(string userName, string password) =>
        new AutoCanvasFactory().WithDatabase(Database).With("Bootstrap:AdminUserName", userName).With("Bootstrap:AdminPassword", password);

    private Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] command) => RunWithInputAsync(string.Empty, command);

    private Task<(int ExitCode, string Output, string Error)> RunWithInputAsync(string input, params string[] command) =>
        DatabaseTests.RunAsync(
            [.. command, $"--Database:Provider={Provider}", $"--Database:ConnectionString={Database.ConnectionString}"],
            input);

    private async Task<Persistence.Identity.User> ReadUserAsync()
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await context.Users.AsNoTracking().SingleAsync(Cancel);
        }
    }
}

[Collection(CommandLineTests.Name)]
public sealed class AdminCommandTestsOnSqlite() : AdminCommandTests(DatabaseProvider.Sqlite);

[Collection(CommandLineTests.Name)]
public sealed class AdminCommandTestsOnPostgres() : AdminCommandTests(DatabaseProvider.Postgres);
