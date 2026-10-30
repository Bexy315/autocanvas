namespace AutoCanvas.Core.Diagnostics;

/// <summary>
/// The codes of all diagnostics. Codes are stable: tools and documentation may refer to them, so a
/// code is never reused for a different problem.
/// </summary>
/// <remarks>
/// <c>AC00xx</c> are problems reading the file, <c>AC01xx</c> problems with the structure of the
/// automation format.
/// </remarks>
public static class DiagnosticCodes
{
    /// <summary>The file is not valid YAML.</summary>
    public const string InvalidYaml = "AC0001";

    /// <summary>The file is larger or more deeply nested than allowed.</summary>
    public const string TooLarge = "AC0002";

    /// <summary>The file is empty or contains more than one YAML document.</summary>
    public const string NotOneDocument = "AC0003";

    /// <summary>A value has the wrong type, for example a list where text is expected.</summary>
    public const string WrongType = "AC0101";

    /// <summary>A required key is missing.</summary>
    public const string MissingKey = "AC0102";

    /// <summary>A key is not part of the format at this place.</summary>
    public const string UnknownKey = "AC0103";

    /// <summary>A value is outside the allowed range or set, or does not have the required form.</summary>
    public const string InvalidValue = "AC0104";

    /// <summary>Keys that exclude each other are used together, or none of the required alternatives is used.</summary>
    public const string ConflictingKeys = "AC0105";

    /// <summary>The <c>version</c> is not one this build can read.</summary>
    public const string UnsupportedVersion = "AC0106";
}
