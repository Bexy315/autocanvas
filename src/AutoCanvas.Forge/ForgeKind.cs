namespace AutoCanvas.Forge;

/// <summary>The kinds of forge AutoCanvas talks to (ADR-0021).</summary>
public enum ForgeKind
{
    /// <summary>Any Git repository over HTTP(S): files and commits, nothing more.</summary>
    GenericGit,

    /// <summary>GitHub through the instance's GitHub App (ADR-0022).</summary>
    GitHub,
}
