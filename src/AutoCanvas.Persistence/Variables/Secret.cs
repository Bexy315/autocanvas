using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Persistence.Variables;

/// <summary>
/// A secret of the instance, a folder or an automation, readable in automations as
/// <c>${{ secrets.NAME }}</c>. Only its ciphertext is stored (ADR-0013); no API returns the value.
/// </summary>
public sealed class Secret
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = Variable.MaxNameLength;

    /// <summary>Largest value, in characters.</summary>
    public const int MaxValueLength = Variable.MaxValueLength;

    /// <summary>Column size of <see cref="KeyId"/>.</summary>
    public const int MaxKeyIdLength = 16;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Where it is defined; see <see cref="ValueScopes"/>. Part of the encryption's associated data.</summary>
    public required string Scope { get; init; }

    /// <summary>The folder, for folder secrets; deleting the folder deletes them.</summary>
    public Guid? FolderId { get; init; }

    /// <summary>The automation, for automation secrets; deleting the automation deletes them.</summary>
    public Guid? AutomationId { get; init; }

    /// <summary>The name as written; expressions read it regardless of case.</summary>
    public required string Name { get; set; }

    /// <summary>Upper-case form of <see cref="Name"/>, unique per scope; part of the associated data.</summary>
    public required string NormalizedName { get; init; }

    /// <summary>Nonce, tag and ciphertext of the value (AES-256-GCM), base64-encoded.</summary>
    public required string Ciphertext { get; set; }

    /// <summary>Which master key encrypted it.</summary>
    public required string KeyId { get; set; }

    /// <summary>When it was first set.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it was last set.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who set it last, as their user name then.</summary>
    public required string UpdatedByName { get; set; }

    /// <summary>Navigation to the folder.</summary>
    public Folder? Folder { get; init; }

    /// <summary>Navigation to the automation.</summary>
    public Automation? Automation { get; init; }
}
