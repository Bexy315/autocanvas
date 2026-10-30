namespace AutoCanvas.Server.Application.Audit;

/// <summary>
/// Who is acting in the current scope and from where. The host provides it: the system for the
/// command line and background work, the signed-in user for HTTP requests (from S1 step 4).
/// </summary>
public interface IAuditContext
{
    /// <summary>The actor of every change in this scope.</summary>
    AuditActor Actor { get; }

    /// <summary>Network address of the client, if the change comes from one.</summary>
    string? ClientAddress { get; }

    /// <summary>User agent of the client, if the change comes from one.</summary>
    string? UserAgent { get; }
}
