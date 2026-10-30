namespace AutoCanvas.Server.Application.Audit;

/// <summary>What a use case reports about one change; the audit log adds who, when and from where.</summary>
/// <param name="Action">An action from <see cref="AuditActions"/>.</param>
public sealed record AuditEntry(string Action)
{
    /// <summary>Kind of object changed, such as <c>folder</c>; empty for instance-wide actions.</summary>
    public string? TargetType { get; init; }

    /// <summary>Identifier of the object changed.</summary>
    public string? TargetId { get; init; }

    /// <summary>The folder the object lives in, so its owners can read this part of the log.</summary>
    public Guid? FolderId { get; init; }

    /// <summary>
    /// A few facts about the change, stored as a JSON object. Never put secret values here —
    /// for a secret, record only its name.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Details { get; init; }
}
