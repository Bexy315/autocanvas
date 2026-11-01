using System.Text.Json;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using AutoCanvas.Core.Nodes;
using AutoCanvas.Core.Parsing;

namespace AutoCanvas.Core.Validation;

/// <summary>
/// Checks node steps against the node types the host knows (ADR-0016): the type must exist, and its
/// inputs must be known, given when required, and of the right kind. A value with an expression is
/// checked when the step runs, since only then is it known.
/// </summary>
internal static class NodeChecks
{
    public static void Check(StepJob job, string path, IReadOnlyDictionary<string, NodeType> nodeTypes, ValidationContext context)
    {
        for (var i = 0; i < job.Steps.Count; i++)
        {
            if (job.Steps[i] is not NodeStep step)
            {
                continue;
            }

            var stepPath = $"{path}.steps[{i}]";
            if (!nodeTypes.TryGetValue(step.Type, out var type))
            {
                context.Error(
                    DiagnosticCodes.InvalidValue,
                    $"{stepPath}.type",
                    $"'{step.Type}' is not a node type this server knows.{Suggestions.ForKey(step.Type, nodeTypes.Keys)} Use one of: {string.Join(", ", nodeTypes.Keys.Order(StringComparer.Ordinal))}.");
                continue;
            }

            CheckInputs(type, step.Inputs, $"{stepPath}.with", context);
        }
    }

    private static void CheckInputs(NodeType type, JsonElement inputs, string path, ValidationContext context)
    {
        var given = new HashSet<string>(StringComparer.Ordinal);
        if (inputs.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in inputs.EnumerateObject())
            {
                given.Add(property.Name);
                var input = type.Inputs.FirstOrDefault(i => string.Equals(i.Name, property.Name, StringComparison.Ordinal));
                if (input is null)
                {
                    context.Error(
                        DiagnosticCodes.UnknownKey,
                        $"{path}.{property.Name}",
                        $"'{property.Name}' is not an input of {type.Name}.{Suggestions.ForKey(property.Name, type.Inputs.Select(i => i.Name))}");
                    continue;
                }

                CheckValue(input, property.Value, $"{path}.{property.Name}", context);
            }
        }

        foreach (var missing in type.Inputs.Where(i => i.Required && !given.Contains(i.Name)))
        {
            context.Error(DiagnosticCodes.MissingKey, path, $"{type.Name} needs the input '{missing.Name}': {missing.Description}");
        }
    }

    private static void CheckValue(NodeInput input, JsonElement value, string path, ValidationContext context)
    {
        if (IsExpression(value))
        {
            return;
        }

        var (fits, expected) = input.Kind switch
        {
            NodeInputKind.Text => (IsScalar(value), "a text"),
            NodeInputKind.WholeNumber => (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _), "a whole number"),
            NodeInputKind.Boolean => (value.ValueKind is JsonValueKind.True or JsonValueKind.False, "true or false"),
            NodeInputKind.TextList => (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(v => IsScalar(v) || IsExpression(v)), "a list of texts"),
            _ => (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(p => IsScalar(p.Value) || IsExpression(p.Value)), "a mapping of names to texts"),
        };
        if (!fits)
        {
            context.Error(DiagnosticCodes.WrongType, path, $"Expected {expected} for '{input.Name}'.");
            return;
        }

        if (input.Choices.Count > 0 && value.ValueKind == JsonValueKind.String
            && !input.Choices.Contains(value.GetString()!, StringComparer.OrdinalIgnoreCase))
        {
            context.Error(DiagnosticCodes.InvalidValue, path, $"'{value.GetString()}' is not allowed for '{input.Name}'. Use one of: {string.Join(", ", input.Choices)}.");
        }
    }

    private static bool IsScalar(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    private static bool IsExpression(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString()!.Contains("${{", StringComparison.Ordinal);
}
