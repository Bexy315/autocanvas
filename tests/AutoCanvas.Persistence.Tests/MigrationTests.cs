using AutoCanvas.Persistence.Audit;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Persistence.Tests;

public abstract class MigrationTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task An_empty_database_reports_every_migration_as_pending()
    {
        var context = Database.CreateContext();
        await using (context)
        {
            var pending = await new DatabaseMigrator(context).GetPendingAsync(Cancel);

            pending.ShouldContain(name => name.EndsWith("_Initial", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Migrating_applies_everything_once()
    {
        var first = await Database.MigrateAsync(cancellationToken: Cancel);
        var second = await Database.MigrateAsync(cancellationToken: Cancel);

        first.ShouldNotBeEmpty();
        second.ShouldBeEmpty();

        var context = Database.CreateContext();
        await using (context)
        {
            (await new DatabaseMigrator(context).GetPendingAsync(Cancel)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Migrating_a_fresh_database_logs_no_errors()
    {
        // A failed query on the missing history table reads like a broken installation.
        using var log = new RecordingLoggerFactory();

        await Database.MigrateAsync(log, Cancel);

        log.Entries.Where(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning).ShouldBeEmpty();
        log.Entries.ShouldContain(e => e.Message.Contains("Applying migration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_model_has_no_change_without_a_migration()
    {
        var context = Database.CreateContext();
        await using (context)
        {
            // Fails when an entity changed but build/add-migration.sh was not run for this provider.
            context.Database.HasPendingModelChanges().ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Audit_events_round_trip_with_utc_timestamps_that_sort_in_the_database()
    {
        await Database.MigrateAsync(cancellationToken: Cancel);
        var start = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        var context = Database.CreateContext();
        await using (context)
        {
            for (var minutes = 0; minutes < 3; minutes++)
            {
                context.AuditEvents.Add(Event(start.AddMinutes(minutes), $"test.event{minutes}"));
            }

            await context.SaveChangesAsync(Cancel);
        }

        var reader = Database.CreateContext();
        await using (reader)
        {
            var newest = await reader.AuditEvents
                .Where(e => e.OccurredAt > start)
                .OrderByDescending(e => e.OccurredAt)
                .ToListAsync(Cancel);

            newest.Select(e => e.Action).ShouldBe(["test.event2", "test.event1"]);
            newest[0].OccurredAt.ShouldBe(start.AddMinutes(2));
            newest[0].OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
            newest[0].ActorKind.ShouldBe(AuditActorKind.System);
            newest[0].Details.ShouldBe("""{"key":"value"}""");
        }
    }

    private static AuditEvent Event(DateTimeOffset occurredAt, string action) => new()
    {
        Id = Guid.CreateVersion7(occurredAt),
        OccurredAt = occurredAt,
        ActorKind = AuditActorKind.System,
        ActorName = "system",
        Action = action,
        Details = """{"key":"value"}""",
    };
}

public sealed class MigrationTestsOnSqlite() : MigrationTests(DatabaseProvider.Sqlite);

public sealed class MigrationTestsOnPostgres() : MigrationTests(DatabaseProvider.Postgres);
