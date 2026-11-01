namespace AutoCanvas.Persistence.Triggers;

/// <summary>
/// The webhook address of an automation: a token, stored only as its hash, and the signing secret
/// for signed calls, encrypted with the master key (ADR-0013). One per automation; creating a new
/// one replaces it.
/// </summary>
public sealed class AutomationWebhook
{
    /// <summary>Column size of <see cref="TokenHash"/>.</summary>
    public const int HashLength = 64;

    /// <summary>Column size of <see cref="Hint"/>.</summary>
    public const int MaxHintLength = 20;

    /// <summary>Column size of <see cref="SigningSecret"/>.</summary>
    public const int MaxSecretLength = 500;

    /// <summary>The automation.</summary>
    public required Guid AutomationId { get; init; }

    /// <summary>SHA-256 of the token, lower-case hex.</summary>
    public required string TokenHash { get; init; }

    /// <summary>The token's start and end, to tell it apart.</summary>
    public required string Hint { get; init; }

    /// <summary>The encrypted signing secret; <see langword="null"/> when calls need no signature.</summary>
    public string? SigningSecret { get; init; }

    /// <summary>When it was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Who created it, at that time.</summary>
    public required string CreatedByName { get; init; }

    /// <summary>Navigation to the automation.</summary>
    public Automations.Automation? Automation { get; init; }
}
