namespace AutoCanvas.Server.Application.Common;

/// <summary>Why a use case refused a request; the host maps each kind to a problem type.</summary>
public enum UseCaseErrorKind
{
    /// <summary>The input breaks a rule, such as a name that is too long.</summary>
    Validation,

    /// <summary>The object does not exist (or the caller may not know that it does).</summary>
    NotFound,

    /// <summary>The request clashes with the current state, such as a taken name.</summary>
    Conflict,

    /// <summary>The caller may not do this.</summary>
    Forbidden,

    /// <summary>The credentials are wrong; deliberately unspecific.</summary>
    Unauthorized,

    /// <summary>Too many attempts; the caller has to wait.</summary>
    TooManyRequests,

    /// <summary>The object changed since the caller read it; re-read and try again.</summary>
    PreconditionFailed,
}
