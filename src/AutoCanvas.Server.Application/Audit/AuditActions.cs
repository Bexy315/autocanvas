namespace AutoCanvas.Server.Application.Audit;

/// <summary>
/// Every action the audit log knows. The names are stable — they are stored and queried — and the
/// catalogue in <c>docs/technical/audit.md</c> lists each with its target and details.
/// </summary>
public static class AuditActions
{
    /// <summary>The database schema was updated; details: <c>migrations</c> (names applied).</summary>
    public const string DatabaseMigrated = "database.migrated";
}
