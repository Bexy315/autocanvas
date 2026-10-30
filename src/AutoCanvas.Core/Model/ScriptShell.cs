namespace AutoCanvas.Core.Model;

/// <summary>Shells a script step can ask for.</summary>
public enum ScriptShell
{
    /// <summary>bash.</summary>
    Bash,

    /// <summary>POSIX sh.</summary>
    Sh,

    /// <summary>PowerShell 7.</summary>
    Pwsh,

    /// <summary>Windows cmd.</summary>
    Cmd,
}
