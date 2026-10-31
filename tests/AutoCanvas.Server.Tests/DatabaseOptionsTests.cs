using AutoCanvas.Persistence;
using AutoCanvas.Server.Database;

namespace AutoCanvas.Server.Tests;

[Collection(CommandLineTests.Name)]
public sealed class DatabaseOptionsTests
{
    [Theory]
    [InlineData(DatabaseProvider.Sqlite, null, true)]
    [InlineData(DatabaseProvider.Sqlite, "Data Source=x.db", true)]
    [InlineData(DatabaseProvider.Postgres, "Host=db;Database=autocanvas", true)]
    [InlineData(DatabaseProvider.Postgres, null, false)]
    [InlineData(DatabaseProvider.Postgres, " ", false)]
    [InlineData((DatabaseProvider)7, null, false)]
    public void Settings_are_checked(DatabaseProvider provider, string? connectionString, bool valid) =>
        new DatabaseOptionsValidator()
            .Validate(null, new DatabaseOptions { Provider = provider, ConnectionString = connectionString })
            .Succeeded.ShouldBe(valid);

    [Fact]
    public async Task The_migrate_command_explains_invalid_settings()
    {
        var (exitCode, _, error) = await DatabaseTests.RunAsync(["migrate", "--Database:Provider=Postgres"]);

        exitCode.ShouldBe(1);
        error.ShouldContain("Database:ConnectionString is required for PostgreSQL");
    }
}
