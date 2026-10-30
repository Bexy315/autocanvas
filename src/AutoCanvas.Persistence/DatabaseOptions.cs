namespace AutoCanvas.Persistence;

/// <summary>Database settings (configuration section <c>Database</c>).</summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Database";

    /// <summary>Which database to use. Defaults to SQLite.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>
    /// Provider-specific connection string. Required for PostgreSQL; for SQLite the default is a
    /// file in the server's data directory.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Apply pending migrations when the server starts. Defaults to <see langword="true"/>.</summary>
    public bool MigrateOnStart { get; set; } = true;
}
