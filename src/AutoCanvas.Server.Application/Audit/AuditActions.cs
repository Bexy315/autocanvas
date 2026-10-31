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

    /// <summary>An API token was created; details: <c>tokenId</c>, <c>name</c>, <c>expiresAt</c>.</summary>
    public const string TokenCreated = "token.created";

    /// <summary>An API token was deleted; details: <c>tokenId</c>, <c>name</c>.</summary>
    public const string TokenDeleted = "token.deleted";

    /// <summary>A folder was created; details: <c>name</c>, <c>parentId</c>.</summary>
    public const string FolderCreated = "folder.created";

    /// <summary>A folder was renamed or described; details: <c>changed</c>, <c>name</c>.</summary>
    public const string FolderUpdated = "folder.updated";

    /// <summary>A folder moved; details: <c>fromParentId</c>, <c>toParentId</c>.</summary>
    public const string FolderMoved = "folder.moved";

    /// <summary>A folder was marked restricted or unmarked; details: <c>isRestricted</c>.</summary>
    public const string FolderRestrictionChanged = "folder.restriction_changed";

    /// <summary>A folder was deleted; details: <c>name</c>, <c>parentId</c>.</summary>
    public const string FolderDeleted = "folder.deleted";

    /// <summary>A role was granted or changed; details: <c>subjectType</c>, <c>subjectId</c>, <c>subjectName</c>, <c>role</c>, <c>previousRole</c>.</summary>
    public const string FolderGrantSet = "folder.grant_set";

    /// <summary>A grant was removed; details: <c>subjectType</c>, <c>subjectId</c>, <c>subjectName</c>, <c>role</c>.</summary>
    public const string FolderGrantRemoved = "folder.grant_removed";

    /// <summary>An automation was created with version 1; details: <c>name</c>.</summary>
    public const string AutomationCreated = "automation.created";

    /// <summary>A new version was saved; details: <c>version</c>, <c>message</c>. Never the file itself.</summary>
    public const string AutomationSaved = "automation.saved";

    /// <summary>An automation got another key in its folder; details: <c>from</c>, <c>to</c>.</summary>
    public const string AutomationRenamed = "automation.renamed";

    /// <summary>An automation moved to another folder; details: <c>fromFolderId</c>, <c>toFolderId</c>.</summary>
    public const string AutomationMoved = "automation.moved";

    /// <summary>An automation was deleted with all its versions; details: <c>name</c>, <c>versions</c>.</summary>
    public const string AutomationDeleted = "automation.deleted";

    /// <summary>A variable of the instance or a folder was created or replaced; details: <c>scope</c>, <c>name</c>. Never the value.</summary>
    public const string VariableSet = "variable.set";

    /// <summary>A variable was deleted; details: <c>scope</c>, <c>name</c>.</summary>
    public const string VariableDeleted = "variable.deleted";

    /// <summary>A secret was created or replaced; details: <c>scope</c>, <c>name</c>. Never the value.</summary>
    public const string SecretSet = "secret.set";

    /// <summary>A secret was deleted; details: <c>scope</c>, <c>name</c>.</summary>
    public const string SecretDeleted = "secret.deleted";

    /// <summary>Every secret was re-encrypted under a new master key; details: <c>fromKeyId</c>, <c>toKeyId</c>, <c>secrets</c>.</summary>
    public const string MasterKeyRotated = "secrets.master_key_rotated";

    /// <summary>Entries older than the retention period were deleted; details: <c>before</c>, <c>deleted</c>.</summary>
    public const string AuditPruned = "audit.pruned";
}
