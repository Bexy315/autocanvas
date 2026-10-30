using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.TestSupport;

/// <summary>A SQLite database in a temporary file, deleted afterwards.</summary>
public sealed class SqliteTestDatabase : TestDatabase
{
    private readonly string path;

    private SqliteTestDatabase(string path)
    {
        this.path = path;
        // No pooling, so no connection keeps the file open when it is deleted.
        ConnectionString = $"Data Source={path};Pooling=False";
    }

    /// <inheritdoc />
    public override DatabaseProvider Provider => DatabaseProvider.Sqlite;

    /// <inheritdoc />
    public override string ConnectionString { get; }

    /// <summary>Path of the database file.</summary>
    public string FilePath => path;

    /// <summary>Creates the (still empty) database file location.</summary>
    public static SqliteTestDatabase Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "autocanvas-tests");
        Directory.CreateDirectory(directory);
        return new SqliteTestDatabase(Path.Combine(directory, $"{Guid.NewGuid():N}.db"));
    }

    /// <inheritdoc />
    public override AutoCanvasDbContext CreateContext(ILoggerFactory? loggerFactory = null)
    {
        var options = new DbContextOptionsBuilder<SqliteAutoCanvasDbContext>();
        options.UseAutoCanvasSqlite(ConnectionString);
        if (loggerFactory is not null)
        {
            options.UseLoggerFactory(loggerFactory);
        }

        return new SqliteAutoCanvasDbContext(options.Options);
    }

    /// <inheritdoc />
    protected override ValueTask DisposeCoreAsync()
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            File.Delete(file);
        }

        return ValueTask.CompletedTask;
    }
}
