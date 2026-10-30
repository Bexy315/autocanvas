using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Database;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Server.Application.Tests;

public abstract class DatabaseMaintenanceTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task Migrating_records_the_applied_migrations_once()
    {
        var first = await MigrateAsync();
        var second = await MigrateAsync();

        first.ShouldNotBeEmpty();
        second.ShouldBeEmpty();

        var context = Database.CreateContext();
        await using (context)
        {
            var e = (await context.AuditEvents.ToListAsync(Cancel)).ShouldHaveSingleItem();
            e.Action.ShouldBe(AuditActions.DatabaseMigrated);
            e.ActorKind.ShouldBe(AuditActorKind.System);
            e.ActorName.ShouldBe("system");
            JsonDocument.Parse(e.Details!).RootElement.GetProperty("migrations").EnumerateArray()
                .Select(m => m.GetString()).ShouldBe(first);
        }
    }

    [Fact]
    public async Task The_registration_resolves_the_use_cases_with_the_system_as_actor()
    {
        var services = new ServiceCollection()
            .AddScoped(_ => Database.CreateContext())
            .AddAutoCanvasApplication()
            .BuildServiceProvider();
        await using (services)
        {
            var scope = services.CreateAsyncScope();
            await using (scope)
            {
                scope.ServiceProvider.GetRequiredService<IAuditContext>().ShouldBeOfType<SystemAuditContext>();
                (await scope.ServiceProvider.GetRequiredService<DatabaseMaintenance>().MigrateAsync(Cancel)).ShouldNotBeEmpty();
            }
        }
    }

    private async Task<IReadOnlyList<string>> MigrateAsync()
    {
        var context = Database.CreateContext();
        await using (context)
        {
            var maintenance = new DatabaseMaintenance(
                context,
                new DatabaseMigrator(context),
                new AuditLog(context, new SystemAuditContext(), TimeProvider.System));
            return await maintenance.MigrateAsync(Cancel);
        }
    }
}

public sealed class DatabaseMaintenanceTestsOnSqlite() : DatabaseMaintenanceTests(DatabaseProvider.Sqlite);

public sealed class DatabaseMaintenanceTestsOnPostgres() : DatabaseMaintenanceTests(DatabaseProvider.Postgres);
