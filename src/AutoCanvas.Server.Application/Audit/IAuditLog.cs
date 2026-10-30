namespace AutoCanvas.Server.Application.Audit;

/// <summary>
/// Records changes in the append-only audit log. An entry is written by the use case's
/// <c>SaveChanges</c>, so it commits — or rolls back — together with the change it describes.
/// </summary>
public interface IAuditLog
{
    /// <summary>Adds an entry for the current actor to the pending changes.</summary>
    /// <exception cref="ArgumentException">The action is not a dotted lower-case name.</exception>
    void Record(AuditEntry entry);
}
