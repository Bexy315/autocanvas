namespace AutoCanvas.Server.Application.Identity;

/// <summary>The result of checking a password against a stored hash.</summary>
public enum PasswordVerification
{
    /// <summary>The password is wrong, or the hash is not one AutoCanvas wrote.</summary>
    Failed,

    /// <summary>The password is right.</summary>
    Succeeded,

    /// <summary>The password is right, but the hash is weaker than new ones; store a new hash.</summary>
    SucceededRehashNeeded,
}
