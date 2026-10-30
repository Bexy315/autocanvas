namespace AutoCanvas.Core.Parsing;

/// <summary>The type a YAML scalar has under the YAML 1.2 core schema.</summary>
internal enum ScalarKind
{
    Null,
    Boolean,
    Integer,
    Float,
    String,
}
