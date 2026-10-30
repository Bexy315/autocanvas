using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Reads the <c>jobs</c> block and dispatches each job by its <c>type</c>.</summary>
internal static class JobReader
{
    public const int MaxTimeoutMinutes = 10080;

    public static IReadOnlyList<JobDefinition> ReadAll(YamlNode? node, string path, ParseContext context)
    {
        var jobs = new List<JobDefinition>();
        if (node is null || Mapping.From(node, path, context, "jobs") is not { } map)
        {
            return jobs;
        }

        if (!map.Node.Children.Any())
        {
            context.Error(DiagnosticCodes.InvalidValue, path, node, "An automation needs at least one job.");
        }

        foreach (var (id, keyNode, value) in map.Entries)
        {
            var jobPath = ParseContext.Child(path, id);
            map.Take(id);
            if (Values.CheckIdentifier(id, jobPath, keyNode, context) && Read(id, value, jobPath, context) is { } job)
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    private static JobDefinition? Read(string id, YamlNode node, string path, ParseContext context)
    {
        if (Mapping.From(node, path, context, "a job") is not { } map)
        {
            return null;
        }

        var type = Values.Text(map.Take("type"), ParseContext.Child(path, "type"), context);
        var header = new JobHeader(
            id,
            Values.NonEmptyText(map.Take("name"), ParseContext.Child(path, "name"), context),
            JobParts.Needs(map.Take("needs"), ParseContext.Child(path, "needs"), context),
            Values.NonEmptyText(map.Take("if"), ParseContext.Child(path, "if"), context));

        if (type is not (null or "switch" or "join"))
        {
            // Which keys are allowed depends on the type, so the remaining keys cannot be judged.
            return InvalidType(type, map, context);
        }

        JobDefinition? job = type switch
        {
            null => StepJobReader.Read(header, map, context),
            "switch" => ReadSwitch(header, map, context),
            _ => ReadJoin(header, map, context),
        };

        map.ReportUnknownKeys(context);
        return job;
    }

    private static JobDefinition? InvalidType(string type, Mapping map, ParseContext context)
    {
        context.Error(
            DiagnosticCodes.InvalidValue,
            ParseContext.Child(map.Path, "type"),
            map.Take("type"),
            $"'{type}' is not a job type. Leave 'type' out for a job with steps, or use 'switch' or 'join'.");
        return null;
    }

    private static SwitchJob? ReadSwitch(JobHeader header, Mapping map, ParseContext context)
    {
        var expression = Values.NonEmptyText(map.Require("expression", context), ParseContext.Child(map.Path, "expression"), context);
        var cases = JobParts.Cases(map.Require("cases", context), ParseContext.Child(map.Path, "cases"), context);
        return expression is null || cases is null
            ? null
            : new SwitchJob(header.Id, header.Name, header.Needs, header.Condition, expression, cases);
    }

    private static JoinJob ReadJoin(JobHeader header, Mapping map, ParseContext context)
    {
        if (!map.Has("needs"))
        {
            context.Error(DiagnosticCodes.MissingKey, map.Path, map.Node, "A join needs 'needs': the jobs it waits for.");
        }

        return new JoinJob(header.Id, header.Name, header.Needs, header.Condition);
    }
}
