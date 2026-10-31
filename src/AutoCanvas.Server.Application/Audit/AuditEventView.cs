using System.Text.Json;
using AutoCanvas.Persistence.Audit;

namespace AutoCanvas.Server.Application.Audit;

/// <summary>An audit entry as the API shows it; fields as described in the audit catalogue.</summary>
/// <param name="Id">The entry's id.</param>
/// <param name="OccurredAt">When the change was committed, in UTC.</param>
/// <param name="ActorKind">System, User, Token or Anonymous.</param>
/// <param name="ActorId">The user behind the change, if any.</param>
/// <param name="ActorName">The actor's name at the time.</param>
/// <param name="Action">What happened, such as <c>folder.created</c>.</param>
/// <param name="TargetType">Kind of object changed.</param>
/// <param name="TargetId">Id of the object changed.</param>
/// <param name="FolderId">The folder the object lives in.</param>
/// <param name="Details">A few facts about the change; never secret values.</param>
/// <param name="ClientAddress">Network address of the client, for changes over HTTP.</param>
/// <param name="UserAgent">User agent of the client, for changes over HTTP.</param>
public sealed record AuditEventView(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditActorKind ActorKind,
    Guid? ActorId,
    string ActorName,
    string Action,
    string? TargetType,
    string? TargetId,
    Guid? FolderId,
    JsonElement? Details,
    string? ClientAddress,
    string? UserAgent)
{
    internal static AuditEventView From(AuditEvent entry) => new(
        entry.Id,
        entry.OccurredAt,
        entry.ActorKind,
        entry.ActorId,
        entry.ActorName,
        entry.Action,
        entry.TargetType,
        entry.TargetId,
        entry.FolderId,
        entry.Details is null ? null : JsonDocument.Parse(entry.Details).RootElement.Clone(),
        entry.ClientAddress,
        entry.UserAgent);
}
