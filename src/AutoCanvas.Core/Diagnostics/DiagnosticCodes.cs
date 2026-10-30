namespace AutoCanvas.Core.Diagnostics;

/// <summary>
/// The codes of all diagnostics. Codes are stable: tools and documentation may refer to them, so a
/// code is never reused for a different problem.
/// </summary>
/// <remarks>
/// <c>AC00xx</c> are problems reading the file, <c>AC01xx</c> problems with the structure of the
/// automation format, <c>AC02xx</c> problems with how its parts fit together, <c>AC03xx</c>
/// problems in expressions.
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

    /// <summary>A job needs a job that does not exist.</summary>
    public const string UnknownJob = "AC0201";

    /// <summary>A job lists the same job twice in <c>needs</c>.</summary>
    public const string DuplicateNeed = "AC0202";

    /// <summary>Jobs need each other in a cycle, so none of them can start.</summary>
    public const string DependencyCycle = "AC0203";

    /// <summary>A job needs a finally job, which only runs after every other job.</summary>
    public const string NeedsFinallyJob = "AC0204";

    /// <summary>A need names a case of a job that is not a switch, or a case the switch does not have.</summary>
    public const string UnknownCase = "AC0205";

    /// <summary>A switch has two cases with the same name, or more than one default case.</summary>
    public const string InvalidCases = "AC0206";

    /// <summary>Two steps of a job have the same id.</summary>
    public const string DuplicateStepId = "AC0207";

    /// <summary>An expression is not valid syntax.</summary>
    public const string ExpressionSyntax = "AC0301";

    /// <summary>An expression reads something that does not exist or is not available at this place.</summary>
    public const string UnknownReference = "AC0302";

    /// <summary>An expression reads a job or step whose values are not available yet at this place.</summary>
    public const string UnreachableReference = "AC0303";
}
