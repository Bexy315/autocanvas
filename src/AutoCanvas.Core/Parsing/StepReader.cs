using System.Text.RegularExpressions;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Reads one step: a script (<c>run</c>) or a node type (<c>type</c> + <c>with</c>).</summary>
internal static partial class StepReader
{
    private static readonly Dictionary<string, ScriptShell> Shells = new(StringComparer.Ordinal)
    {
        ["bash"] = ScriptShell.Bash,
        ["sh"] = ScriptShell.Sh,
        ["pwsh"] = ScriptShell.Pwsh,
        ["cmd"] = ScriptShell.Cmd,
    };

    public static StepDefinition? Read(YamlNode node, string path, ParseContext context)
    {
        if (Mapping.From(node, path, context, "a step") is not { } map)
        {
            return null;
        }

        var isScript = map.Has("run");
        var isNode = map.Has("type");
        if (isScript == isNode)
        {
            context.Error(DiagnosticCodes.ConflictingKeys, path, node, "A step needs exactly one of 'run' (a script) and 'type' (a node type).");
            return null;
        }

        var common = ReadCommon(map, context);
        StepDefinition? step = isScript ? ReadScript(common, map, context) : ReadNode(common, map, context);
        map.ReportUnknownKeys(context);
        return step;
    }

    private static StepCommon ReadCommon(Mapping map, ParseContext context)
    {
        var path = map.Path;
        return new StepCommon(
            Values.Identifier(map.Take("id"), ParseContext.Child(path, "id"), context),
            Values.NonEmptyText(map.Take("name"), ParseContext.Child(path, "name"), context),
            Values.NonEmptyText(map.Take("if"), ParseContext.Child(path, "if"), context),
            Collections.VariableMap(map.Take("env"), ParseContext.Child(path, "env"), context) ?? Empty.Map,
            Values.Boolean(map.Take("continue_on_error"), ParseContext.Child(path, "continue_on_error"), context) ?? false,
            Values.Integer(map.Take("timeout_minutes"), ParseContext.Child(path, "timeout_minutes"), context, 1, JobReader.MaxTimeoutMinutes));
    }

    private static ScriptStep? ReadScript(StepCommon common, Mapping map, ParseContext context)
    {
        var path = map.Path;
        var run = Values.NonEmptyText(map.Take("run"), ParseContext.Child(path, "run"), context);
        var shell = Values.Choice(map.Take("shell"), ParseContext.Child(path, "shell"), context, Shells);
        var workingDirectory = Values.NonEmptyText(map.Take("working_directory"), ParseContext.Child(path, "working_directory"), context);
        return run is null
            ? null
            : new ScriptStep(common.Id, common.Name, common.Condition, common.Env, common.ContinueOnError, common.TimeoutMinutes, run, shell, workingDirectory);
    }

    private static NodeStep? ReadNode(StepCommon common, Mapping map, ParseContext context)
    {
        var path = map.Path;
        var typePath = ParseContext.Child(path, "type");
        var typeNode = map.Take("type");
        var type = Values.Text(typeNode, typePath, context);
        var withNode = map.Take("with");
        if (withNode is not null and not YamlMappingNode)
        {
            context.Error(DiagnosticCodes.WrongType, ParseContext.Child(path, "with"), withNode, "Expected the node's inputs as a mapping of keys and values.");
        }

        var outputs = Collections.OutputMap(map.Take("outputs"), ParseContext.Child(path, "outputs"), context) ?? Empty.Map;
        if (type is not null && !NodeTypePattern().IsMatch(type))
        {
            context.Error(DiagnosticCodes.InvalidValue, typePath, typeNode, $"'{type}' is not a valid node type. Node types look like 'http_request' or 'publisher/node_name'.");
            return null;
        }

        return type is null
            ? null
            : new NodeStep(common.Id, common.Name, common.Condition, common.Env, common.ContinueOnError, common.TimeoutMinutes, type, JsonValues.ToElement(withNode as YamlMappingNode), outputs);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*(?:/[a-z][a-z0-9_]*)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NodeTypePattern();

    private sealed record StepCommon(string? Id, string? Name, string? Condition, IReadOnlyDictionary<string, string> Env, bool ContinueOnError, int? TimeoutMinutes);
}
