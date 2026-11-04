using AutoCanvas.Persistence.Triggers;

namespace AutoCanvas.Persistence.Repositories;

/// <summary>
/// The webhook address of a connected repository, which its forge calls on pushes: a random token
/// in the path, stored only as its hash, and a secret the forge signs (or sends) with, encrypted with
/// the master key.
/// </summary>
public sealed class RepositoryWebhook
{
    /// <summary>The repository; one address each.</summary>
    public required Guid RepositoryId { get; init; }

    /// <summary>SHA-256 of the token, hex (<see cref="AutomationWebhook.HashLength"/> characters).</summary>
    public required string TokenHash { get; init; }

    /// <summary>The token's start and end, to recognize it by.</summary>
    public required string Hint { get; init; }

    /// <summary>The secret, encrypted with the master key.</summary>
    public required string SecretCiphertext { get; set; }

    /// <summary>When it was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Who created it, kept when the user is deleted.</summary>
    public required string CreatedByName { get; init; }
}
