namespace AutoCanvas.Server.Application.Audit;

/// <summary>
/// Every action the audit log knows. The names are stable — they are stored and queried — and the
/// catalogue in <c>docs/technical/audit.md</c> lists each with its target and details.
/// </summary>
public static class AuditActions
{
    /// <summary>The database schema was updated; details: <c>migrations</c> (names applied).</summary>
    public const string DatabaseMigrated = "database.migrated";

    /// <summary>An account was created; details: <c>userName</c>, <c>isAdmin</c>.</summary>
    public const string UserCreated = "user.created";

    /// <summary>An account was changed; details: <c>changed</c> (field names), <c>isAdmin</c>, <c>isDisabled</c>.</summary>
    public const string UserUpdated = "user.updated";

    /// <summary>An account was deleted; details: <c>userName</c>.</summary>
    public const string UserDeleted = "user.deleted";

    /// <summary>A user changed their own password.</summary>
    public const string UserPasswordChanged = "user.password_changed";

    /// <summary>Someone else set a user's password; the user must change it.</summary>
    public const string UserPasswordReset = "user.password_reset";

    /// <summary>A group was created; details: <c>name</c>.</summary>
    public const string GroupCreated = "group.created";

    /// <summary>A group was renamed or described; details: <c>changed</c>, <c>name</c>.</summary>
    public const string GroupUpdated = "group.updated";

    /// <summary>A group was deleted; details: <c>name</c>.</summary>
    public const string GroupDeleted = "group.deleted";

    /// <summary>A user joined a group; details: <c>userId</c>, <c>userName</c>.</summary>
    public const string GroupMemberAdded = "group.member_added";

    /// <summary>A user left a group; details: <c>userId</c>, <c>userName</c>.</summary>
    public const string GroupMemberRemoved = "group.member_removed";

    /// <summary>A user signed in; details: <c>sessionId</c>.</summary>
    public const string SignedIn = "auth.signed_in";

    /// <summary>
    /// A sign-in failed (unknown name, wrong password, disabled user or too many attempts);
    /// details: <c>userName</c> as typed, <c>reason</c>.
    /// </summary>
    public const string SignInFailed = "auth.sign_in_failed";

    /// <summary>A session ended on request; details: <c>sessionId</c>.</summary>
    public const string SignedOut = "auth.signed_out";

    /// <summary>A user ended all their sessions; details: <c>count</c>.</summary>
    public const string SessionsEnded = "auth.sessions_ended";
}
