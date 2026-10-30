namespace AutoCanvas.Server.Application.Folders;

/// <summary>Who a grant is for: exactly one user or one group, with its name for the audit log.</summary>
/// <param name="UserId">The user, for a user grant.</param>
/// <param name="GroupId">The group, for a group grant.</param>
/// <param name="Name">The user name or group name.</param>
internal sealed record GrantSubject(Guid? UserId, Guid? GroupId, string Name)
{
    public string Type => UserId is null ? "group" : "user";

    public Guid Id => UserId ?? GroupId!.Value;

    public static GrantSubject User(Guid id, string name) => new(id, null, name);

    public static GrantSubject Group(Guid id, string name) => new(null, id, name);
}
