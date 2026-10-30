namespace AutoCanvas.Persistence.Audit;

/// <summary>
/// One recorded change: who did what to which object, when and from where. Rows are only ever
/// inserted; they never contain secret values.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>Column size of <see cref="ActorName"/>.</summary>
    public const int MaxActorNameLength = 200;

    /// <summary>Column size of <see cref="Action"/>.</summary>
    public const int MaxActionLength = 100;

    /// <summary>Column size of <see cref="TargetType"/>.</summary>
    public const int MaxTargetTypeLength = 50;

    /// <summary>Column size of <see cref="TargetId"/>.</summary>
    public const int MaxTargetIdLength = 100;

    /// <summary>Column size of <see cref="ClientAddress"/>; fits any IPv6 address.</summary>
    public const int MaxClientAddressLength = 64;

    /// <summary>Column size of <see cref="UserAgent"/>; longer values are cut.</summary>
    public const int MaxUserAgentLength = 500;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>When the change was committed, in UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>What kind of actor made the change.</summary>
    public required AuditActorKind ActorKind { get; init; }

    /// <summary>The user behind the change; empty for the system.</summary>
    public Guid? ActorId { get; init; }

    /// <summary>The actor's name at the time, kept even if the user is renamed or deleted.</summary>
    public required string ActorName { get; init; }

    /// <summary>Dotted action name such as <c>database.migrated</c>; see the audit catalogue.</summary>
    public required string Action { get; init; }

    /// <summary>Kind of object changed, such as <c>folder</c>; empty for instance-wide actions.</summary>
    public string? TargetType { get; init; }

    /// <summary>Identifier of the object changed.</summary>
    public string? TargetId { get; init; }

    /// <summary>The folder the object lives in, so folder owners can read their part of the log.</summary>
    public Guid? FolderId { get; init; }

    /// <summary>Small JSON object with what changed. Never contains secret values.</summary>
    public string? Details { get; init; }

    /// <summary>Network address of the client, when the change came over HTTP.</summary>
    public string? ClientAddress { get; init; }

    /// <summary>User agent of the client, shortened to the column size.</summary>
    public string? UserAgent { get; init; }
}
