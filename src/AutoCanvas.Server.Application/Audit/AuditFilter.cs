namespace AutoCanvas.Server.Application.Audit;

/// <summary>Which audit entries to return; every criterion is optional and they combine.</summary>
/// <param name="From">Entries at or after this time.</param>
/// <param name="To">Entries before this time.</param>
/// <param name="Action">An action such as <c>folder.created</c>, or a prefix ending in <c>*</c> such as <c>folder.*</c>.</param>
/// <param name="ActorId">Entries by this user (in person or through their tokens).</param>
/// <param name="TargetType">Entries about this kind of object, such as <c>automation</c>.</param>
/// <param name="TargetId">Entries about the object with this id; usually with <paramref name="TargetType"/>.</param>
public sealed record AuditFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Action = null,
    Guid? ActorId = null,
    string? TargetType = null,
    string? TargetId = null);
