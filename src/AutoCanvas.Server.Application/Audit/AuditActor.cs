using AutoCanvas.Persistence.Audit;

namespace AutoCanvas.Server.Application.Audit;

/// <summary>Who makes a change: the server itself, or a user (directly or through a token).</summary>
/// <param name="Kind">How the actor is authenticated.</param>
/// <param name="Id">The user's id; <see langword="null"/> for the system.</param>
/// <param name="Name">Display name at the time of the change.</param>
public sealed record AuditActor(AuditActorKind Kind, Guid? Id, string Name)
{
    /// <summary>The server, a command-line tool or a background job.</summary>
    public static AuditActor System { get; } = new(AuditActorKind.System, null, "system");

    /// <summary>A client that is not signed in.</summary>
    public static AuditActor Anonymous { get; } = new(AuditActorKind.Anonymous, null, "anonymous");
}
