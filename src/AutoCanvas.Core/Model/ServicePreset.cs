namespace AutoCanvas.Core.Model;

/// <summary>Services AutoCanvas knows how to start and check.</summary>
public enum ServicePreset
{
    /// <summary>PostgreSQL.</summary>
    Postgres,

    /// <summary>MySQL.</summary>
    MySql,

    /// <summary>Redis.</summary>
    Redis,
}
