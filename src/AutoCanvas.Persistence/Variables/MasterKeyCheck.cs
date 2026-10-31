namespace AutoCanvas.Persistence.Variables;

/// <summary>
/// A known text encrypted with the master key, so the server notices a wrong key when it starts
/// rather than when a run first needs a secret. There is at most one row.
/// </summary>
public sealed class MasterKeyCheck
{
    /// <summary>The only row's id.</summary>
    public const int SingletonId = 1;

    /// <summary>Always <see cref="SingletonId"/>.</summary>
    public required int Id { get; init; }

    /// <summary>Which master key encrypted <see cref="Ciphertext"/>; named in the error when it does not match.</summary>
    public required string KeyId { get; set; }

    /// <summary>The known text, encrypted like a secret and base64-encoded.</summary>
    public required string Ciphertext { get; set; }
}
