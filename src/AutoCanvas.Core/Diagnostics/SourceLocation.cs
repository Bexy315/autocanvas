using System.Runtime.InteropServices;

namespace AutoCanvas.Core.Diagnostics;

/// <summary>A position in an automation file, as shown to users.</summary>
/// <param name="Line">Line number, starting at 1.</param>
/// <param name="Column">Column number, starting at 1.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SourceLocation(int Line, int Column)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Line}:{Column}";
}
