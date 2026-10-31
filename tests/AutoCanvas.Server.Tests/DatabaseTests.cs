using System.Net;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Hosting;
using Microsoft.EntityFrameworkCore;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

public abstract class DatabaseTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task The_server_migrates_on_start_and_is_ready()
    {
        await using var factory = new AutoCanvasFactory().WithDatabase(Database);

        var response = await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var context = Database.CreateContext();
        await using (context)
        {
            (await new DatabaseMigrator(context).GetPendingAsync(Cancel)).ShouldBeEmpty();
            (await context.AuditEvents.SingleAsync(Cancel)).Action.ShouldBe("database.migrated");
        }
    }

    [Fact]
    public async Task Without_migrating_on_start_a_fresh_database_is_not_ready()
    {
        await using var factory = new AutoCanvasFactory().WithDatabase(Database).With("Database:MigrateOnStart", "false");

        var response = await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel)).RootElement;
        body.GetProperty("checks").GetProperty("database").GetString().ShouldBe("Unhealthy");
    }

    [Fact]
    public async Task The_migrate_command_applies_migrations_once()
    {
        string[] args = ["migrate", $"--Database:Provider={Provider}", $"--Database:ConnectionString={Database.ConnectionString}"];

        var (first, firstOutput, _) = await RunAsync(args);
        var (second, secondOutput, _) = await RunAsync(args);

        first.ShouldBe(0);
        firstOutput.ShouldStartWith("Applied ");
        firstOutput.ShouldContain(" migration(s): ");
        firstOutput.ShouldContain("_Initial");
        second.ShouldBe(0);
        secondOutput.ShouldStartWith("The database is up to date.");

        var context = Database.CreateContext();
        await using (context)
        {
            var migrated = await context.AuditEvents.SingleAsync(Cancel);
            migrated.Action.ShouldBe("database.migrated");
            migrated.ActorName.ShouldBe("system");
        }
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args, string input = "")
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var reader = new StringReader(input);
        var exitCode = await ServerCommandLine.RunAsync(args, output, error, reader);
        return (exitCode, output.ToString(), error.ToString());
    }
}

[Collection(CommandLineTests.Name)]
public sealed class DatabaseTestsOnSqlite() : DatabaseTests(DatabaseProvider.Sqlite)
{
    [Fact]
    public async Task Without_settings_the_database_is_a_file_in_the_data_directory()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "autocanvas-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (exitCode, _, _) = await RunAsync(["migrate", $"--Server:DataDirectory={dataDirectory}"]);

            exitCode.ShouldBe(0);
            File.Exists(Path.Combine(dataDirectory, "autocanvas.db")).ShouldBeTrue();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task An_unreachable_database_is_not_ready()
    {
        var missing = Path.Combine(Path.GetTempPath(), "autocanvas-tests", Guid.NewGuid().ToString("N"), "missing.db");
        await using var factory = new AutoCanvasFactory()
            .With("Database:ConnectionString", $"Data Source={missing};Mode=ReadOnly")
            .With("Database:MigrateOnStart", "false");

        var response = await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}

[Collection(CommandLineTests.Name)]
public sealed class DatabaseTestsOnPostgres() : DatabaseTests(DatabaseProvider.Postgres);
