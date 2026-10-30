using AutoCanvas.Persistence;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.TestSupport;

/// <summary>
/// A fresh, empty database for one test, on either provider. Tests that touch the database run
/// on both (ADR-0010): write them once in an abstract class taking a <see cref="DatabaseProvider"/>
/// and derive one sealed class per provider.
/// </summary>
public abstract class TestDatabase : IAsyncDisposable
{
    /// <summary>The provider this database runs on.</summary>
    public abstract DatabaseProvider Provider { get; }

    /// <summary>Connection string of this database, for the server's configuration.</summary>
    public abstract string ConnectionString { get; }

    /// <summary>Settings that point the server at this database.</summary>
    public IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Database:Provider"] = Provider.ToString(),
        ["Database:ConnectionString"] = ConnectionString,
    };

    /// <summary>Creates an empty database; skips the test if PostgreSQL is unavailable locally.</summary>
    public static async Task<TestDatabase> CreateAsync(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => SqliteTestDatabase.Create(),
        DatabaseProvider.Postgres => await PostgresTestDatabase.CreateAsync().ConfigureAwait(false),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown database provider."),
    };

    /// <summary>A new context on this database; the caller disposes it.</summary>
    /// <param name="loggerFactory">Receives EF Core's log, e.g. to assert that nothing failed.</param>
    public abstract AutoCanvasDbContext CreateContext(ILoggerFactory? loggerFactory = null);

    /// <summary>Applies all migrations.</summary>
    public async Task<IReadOnlyList<string>> MigrateAsync(ILoggerFactory? loggerFactory = null, CancellationToken cancellationToken = default)
    {
        var context = CreateContext(loggerFactory);
        await using (context.ConfigureAwait(false))
        {
            return await new DatabaseMigrator(context).MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Removes the database.</summary>
    protected abstract ValueTask DisposeCoreAsync();
}
