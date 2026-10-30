using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Persistence.Tests;

public sealed class SqliteTests() : DatabaseTest(DatabaseProvider.Sqlite)
{
    [Fact]
    public async Task Migrating_switches_to_write_ahead_logging()
    {
        await Database.MigrateAsync(cancellationToken: Cancel);

        var context = Database.CreateContext();
        await using (context)
        {
            var mode = await context.Database.SqlQueryRaw<string>("SELECT journal_mode AS \"Value\" FROM pragma_journal_mode()").SingleAsync(Cancel);

            mode.ShouldBe("wal");
        }
    }
}
