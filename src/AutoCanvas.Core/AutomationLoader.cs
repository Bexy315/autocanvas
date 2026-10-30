using AutoCanvas.Core.Parsing;
using AutoCanvas.Core.Validation;

namespace AutoCanvas.Core;

/// <summary>Reads an automation file completely: parsing, then validation.</summary>
public static class AutomationLoader
{
    /// <summary>
    /// Parses and validates the text of an automation file. Validation only runs when parsing
    /// succeeded, because it needs the whole structure.
    /// </summary>
    /// <param name="yaml">The file's content.</param>
    /// <returns>The automation if the file has no errors at all, with the diagnostics of both stages.</returns>
    public static ParseResult Load(string yaml)
    {
        var parsed = AutomationParser.Parse(yaml);
        if (parsed.Definition is null)
        {
            return parsed;
        }

        var problems = AutomationValidator.Validate(parsed.Definition, parsed.Locations);
        return problems.Count == 0
            ? parsed
            : parsed with { Definition = null, Diagnostics = [.. parsed.Diagnostics, .. problems] };
    }
}
