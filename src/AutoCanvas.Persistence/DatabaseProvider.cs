namespace AutoCanvas.Persistence;

/// <summary>The databases AutoCanvas can store its data in (ADR-0010).</summary>
public enum DatabaseProvider
{
    /// <summary>A single file next to the server; for single-node installations.</summary>
    Sqlite,

    /// <summary>PostgreSQL; recommended for production and required for several server instances.</summary>
    Postgres,
}
