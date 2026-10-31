namespace AutoCanvas.Persistence.Automations;

/// <summary>One saved state of an automation. Versions are never changed, only added.</summary>
public sealed class AutomationVersion
{
    /// <summary>Column size of <see cref="Message"/>.</summary>
    public const int MaxMessageLength = 500;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The automation this is a version of.</summary>
    public required Guid AutomationId { get; init; }

    /// <summary>1 for the first version, counting up with every save.</summary>
    public required int Number { get; init; }

    /// <summary>The automation file, exactly as saved; always valid when it was saved.</summary>
    public required string Yaml { get; init; }

    /// <summary>What changed, in the author's words.</summary>
    public string? Message { get; init; }

    /// <summary>When the version was saved.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The author; empty once the user is deleted.</summary>
    public Guid? CreatedById { get; init; }

    /// <summary>The author's user name at the time, kept when the user is deleted.</summary>
    public required string CreatedByName { get; init; }

    /// <summary>Navigation to the automation.</summary>
    public Automation? Automation { get; init; }
}
