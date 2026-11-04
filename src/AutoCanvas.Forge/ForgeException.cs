namespace AutoCanvas.Forge;

/// <summary>
/// A forge could not do what was asked; the message says why in words for people and never
/// contains a credential.
/// </summary>
public sealed class ForgeException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ForgeException()
    {
    }

    /// <summary>Creates the exception with a message for people.</summary>
    public ForgeException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message for people and its cause.</summary>
    public ForgeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
