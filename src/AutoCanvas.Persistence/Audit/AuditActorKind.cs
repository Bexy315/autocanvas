namespace AutoCanvas.Persistence.Audit;

/// <summary>Who made an audited change.</summary>
public enum AuditActorKind
{
    /// <summary>The server itself, a command-line tool or a background job.</summary>
    System,

    /// <summary>A user signed in with a session.</summary>
    User,

    /// <summary>A user acting through one of their API tokens.</summary>
    Token,

    /// <summary>A client that is not signed in, such as a failed sign-in attempt.</summary>
    Anonymous,
}
