using System.Text.Json;
using System.Text.RegularExpressions;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;

namespace AutoCanvas.Server.Application.Audit;

/// <summary>Adds audit events to the scope's <see cref="AutoCanvasDbContext"/>.</summary>
internal sealed partial class AuditLog(AutoCanvasDbContext database, IAuditContext context, TimeProvider time) : IAuditLog
{
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsAction(entry.Action))
        {
            throw new ArgumentException($"'{entry.Action}' is not an audit action; use a dotted lower-case name from AuditActions.", nameof(entry));
        }

        var now = time.GetUtcNow();
        var actor = entry.Actor ?? context.Actor;
        database.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.CreateVersion7(now),
            OccurredAt = now,
            ActorKind = actor.Kind,
            ActorId = actor.Id,
            ActorName = Cut(actor.Name, AuditEvent.MaxActorNameLength)!,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            FolderId = entry.FolderId,
            Details = entry.Details is { Count: > 0 } details ? JsonSerializer.Serialize(details, JsonSerializerOptions.Web) : null,
            ClientAddress = Cut(context.ClientAddress, AuditEvent.MaxClientAddressLength),
            UserAgent = Cut(context.UserAgent, AuditEvent.MaxUserAgentLength),
        });
    }

    /// <summary>Whether a name has the form of an audit action and fits its column.</summary>
    internal static bool IsAction(string action) =>
        action.Length <= AuditEvent.MaxActionLength && ActionName().IsMatch(action);

    // Values that come from clients may be longer than their column; the log must not fail for it.
    private static string? Cut(string? value, int length) =>
        value is { Length: > 0 } && value.Length > length ? value[..length] : value;

    [GeneratedRegex("^[a-z][a-z_]*(\\.[a-z][a-z_]*)+$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 100)]
    private static partial Regex ActionName();
}
