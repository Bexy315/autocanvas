namespace AutoCanvas.Server.Application.Audit;

/// <summary>Changes made by the server itself: no client, actor <see cref="AuditActor.System"/>.</summary>
public sealed class SystemAuditContext : IAuditContext
{
    /// <inheritdoc />
    public AuditActor Actor => AuditActor.System;

    /// <inheritdoc />
    public string? ClientAddress => null;

    /// <inheritdoc />
    public string? UserAgent => null;
}
