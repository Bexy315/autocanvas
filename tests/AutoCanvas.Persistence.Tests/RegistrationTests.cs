using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Persistence.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public async Task Postgres_registration_resolves_the_shared_context_to_the_postgres_one()
    {
        await using var services = new ServiceCollection().AddAutoCanvasPostgres("Host=db;Database=autocanvas").BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>();

        context.ShouldBeOfType<PostgresAutoCanvasDbContext>();
        context.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Fact]
    public async Task Sqlite_registration_resolves_the_shared_context_to_the_sqlite_one()
    {
        await using var services = new ServiceCollection().AddAutoCanvasSqlite("Data Source=x.db").BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>();

        context.ShouldBeOfType<SqliteAutoCanvasDbContext>();
        context.Database.ProviderName.ShouldBe("Microsoft.EntityFrameworkCore.Sqlite");
    }

    [Fact]
    public void Design_time_factories_create_contexts_with_snake_case_tables()
    {
        using var postgres = new PostgresDesignTimeFactory().CreateDbContext([]);
        using var sqlite = new SqliteDesignTimeFactory().CreateDbContext([]);

        postgres.Model.FindEntityType(typeof(Audit.AuditEvent))!.GetTableName().ShouldBe("audit_events");
        sqlite.Model.FindEntityType(typeof(Audit.AuditEvent))!.GetTableName().ShouldBe("audit_events");
    }
}
