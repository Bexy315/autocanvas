using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Reads a job that runs steps on a runner.</summary>
internal static class StepJobReader
{
    public static StepJob? Read(JobHeader header, Mapping map, ParseContext context)
    {
        var path = map.Path;
        var steps = ReadSteps(map.Require("steps", context), ParseContext.Child(path, "steps"), context);
        var runsFinally = Values.Boolean(map.Take("finally"), ParseContext.Child(path, "finally"), context) ?? false;
        var runsOnCancel = ReadOnCancel(map, runsFinally, context);
        if (runsFinally && map.Has("needs"))
        {
            context.Error(DiagnosticCodes.ConflictingKeys, ParseContext.Child(path, "needs"), map.Node, "A finally job runs after all other jobs and cannot have 'needs'.");
        }

        var job = new StepJob(
            header.Id,
            header.Name,
            header.Needs,
            header.Condition,
            RunsOn: Collections.TextOrList(map.Take("runs_on"), ParseContext.Child(path, "runs_on"), context) ?? [],
            Image: Values.NonEmptyText(map.Take("image"), ParseContext.Child(path, "image"), context),
            Checkout: JobParts.Checkout(map.Take("checkout"), ParseContext.Child(path, "checkout"), context),
            Env: Collections.VariableMap(map.Take("env"), ParseContext.Child(path, "env"), context) ?? Empty.Map,
            Services: JobParts.Services(map.Take("services"), ParseContext.Child(path, "services"), context),
            Steps: steps ?? [],
            Outputs: Collections.OutputMap(map.Take("outputs"), ParseContext.Child(path, "outputs"), context) ?? Empty.Map,
            Reports: JobParts.Reports(map.Take("reports"), ParseContext.Child(path, "reports"), context),
            TimeoutMinutes: Values.Integer(map.Take("timeout_minutes"), ParseContext.Child(path, "timeout_minutes"), context, 1, JobReader.MaxTimeoutMinutes),
            Retry: JobParts.Retry(map.Take("retry"), ParseContext.Child(path, "retry"), context),
            RunsFinally: runsFinally,
            RunsOnCancel: runsOnCancel);
        return steps is null ? null : job;
    }

    private static bool ReadOnCancel(Mapping map, bool runsFinally, ParseContext context)
    {
        var path = ParseContext.Child(map.Path, "on_cancel");
        var node = map.Take("on_cancel");
        var onCancel = Values.Boolean(node, path, context) ?? false;
        if (node is not null && !runsFinally)
        {
            context.Error(DiagnosticCodes.ConflictingKeys, path, node, "'on_cancel' only applies to a job with 'finally: true'.");
        }

        return onCancel;
    }

    private static List<StepDefinition>? ReadSteps(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not YamlSequenceNode { Children.Count: > 0 } sequence)
        {
            context.Error(DiagnosticCodes.WrongType, path, node, "Expected a non-empty list of steps.");
            return null;
        }

        var steps = new List<StepDefinition>();
        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (StepReader.Read(sequence.Children[i], ParseContext.Item(path, i), context) is { } step)
            {
                steps.Add(step);
            }
        }

        return steps;
    }
}
