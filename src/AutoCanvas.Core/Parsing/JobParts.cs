using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Reads the parts of a job: needs, cases, checkout, services, reports and retry.</summary>
internal static class JobParts
{
    private static readonly Dictionary<string, ServicePreset> Presets = new(StringComparer.Ordinal)
    {
        ["postgres"] = ServicePreset.Postgres,
        ["mysql"] = ServicePreset.MySql,
        ["redis"] = ServicePreset.Redis,
    };

    private static readonly HashSet<string> ReportFormats = new(StringComparer.Ordinal) { "junit", "trx", "cobertura" };

    public static IReadOnlyList<JobNeed> Needs(YamlNode? node, string path, ParseContext context)
    {
        var needs = new List<JobNeed>();
        if (node is null)
        {
            return needs;
        }

        if (node is not YamlSequenceNode { Children.Count: > 0 } sequence)
        {
            context.Error(DiagnosticCodes.WrongType, path, node, "Expected a non-empty list of job ids, such as 'needs: [build]'.");
            return needs;
        }

        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (Need(sequence.Children[i], ParseContext.Item(path, i), context) is { } need)
            {
                needs.Add(need);
            }
        }

        return needs;
    }

    private static JobNeed? Need(YamlNode node, string path, ParseContext context)
    {
        if (node is YamlScalarNode)
        {
            return Values.Identifier(node, path, context) is { } id ? new JobNeed(id, OutcomeFilter.Success, Case: null) : null;
        }

        if (Mapping.From(node, path, context, "a need (a job id, or 'job' with 'on' or 'case')") is not { } map)
        {
            return null;
        }

        var job = Values.Identifier(map.Require("job", context), ParseContext.Child(path, "job"), context);
        var on = Values.Choice(map.Take("on"), ParseContext.Child(path, "on"), context, TriggersReader.OutcomeNames);
        var @case = Values.Identifier(map.Take("case"), ParseContext.Child(path, "case"), context);
        map.ReportUnknownKeys(context);
        if (map.Has("on") && map.Has("case"))
        {
            context.Error(DiagnosticCodes.ConflictingKeys, path, node, "Use either 'on' or 'case': a case of a switch is only activated when the switch succeeds.");
            return null;
        }

        return job is null ? null : new JobNeed(job, on ?? OutcomeFilter.Success, @case);
    }

    public static IReadOnlyList<SwitchCase>? Cases(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not YamlSequenceNode { Children.Count: > 0 } sequence)
        {
            context.Error(DiagnosticCodes.WrongType, path, node, "Expected a non-empty list of cases.");
            return null;
        }

        var cases = new List<SwitchCase>();
        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (Case(sequence.Children[i], ParseContext.Item(path, i), context) is { } item)
            {
                cases.Add(item);
            }
        }

        return cases;
    }

    private static SwitchCase? Case(YamlNode node, string path, ParseContext context)
    {
        if (Mapping.From(node, path, context, "a case") is not { } map)
        {
            return null;
        }

        var name = Values.Identifier(map.Require("name", context), ParseContext.Child(path, "name"), context);
        var when = Values.ScalarText(map.Take("when"), ParseContext.Child(path, "when"), context);
        var defaultNode = map.Take("default");
        var isDefault = Values.Boolean(defaultNode, ParseContext.Child(path, "default"), context);
        map.ReportUnknownKeys(context);

        if (isDefault == false)
        {
            context.Error(DiagnosticCodes.InvalidValue, ParseContext.Child(path, "default"), defaultNode, "Write 'default: true' or leave it out.");
            return null;
        }

        if (map.Has("when") == map.Has("default"))
        {
            context.Error(DiagnosticCodes.ConflictingKeys, path, node, "A case needs exactly one of 'when' and 'default: true'.");
            return null;
        }

        return name is null ? null : new SwitchCase(name, when, isDefault == true);
    }

    public static CheckoutSetting Checkout(YamlNode? node, string path, ParseContext context)
    {
        if (node is null)
        {
            return CheckoutSetting.Default;
        }

        if (node is YamlScalarNode scalar && Scalars.KindOf(scalar) == ScalarKind.Boolean && !Scalars.IsTrue(scalar.Value!))
        {
            return CheckoutSetting.Disabled;
        }

        if (node is not YamlSequenceNode { Children.Count: > 0 } sequence)
        {
            context.Error(DiagnosticCodes.WrongType, path, node, "Expected 'false' or a non-empty list of repositories.");
            return CheckoutSetting.Default;
        }

        var repositories = new List<CheckoutRepository>();
        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (Repository(sequence.Children[i], ParseContext.Item(path, i), context) is { } repository)
            {
                repositories.Add(repository);
            }
        }

        return new CheckoutSetting(CheckoutKind.Explicit, repositories);
    }

    private static CheckoutRepository? Repository(YamlNode node, string path, ParseContext context)
    {
        if (Mapping.From(node, path, context, "a repository to check out") is not { } map)
        {
            return null;
        }

        var repository = Values.NonEmptyText(map.Require("repository", context), ParseContext.Child(path, "repository"), context);
        var @ref = Values.NonEmptyText(map.Take("ref"), ParseContext.Child(path, "ref"), context);
        var directory = Values.NonEmptyText(map.Take("path"), ParseContext.Child(path, "path"), context);
        var depth = Values.Integer(map.Take("depth"), ParseContext.Child(path, "depth"), context, 0, int.MaxValue);
        map.ReportUnknownKeys(context);
        return repository is null ? null : new CheckoutRepository(repository, @ref, directory, depth);
    }

    public static IReadOnlyList<ServiceDefinition> Services(YamlNode? node, string path, ParseContext context)
    {
        var services = new List<ServiceDefinition>();
        if (node is null || Mapping.From(node, path, context, "services") is not { } map)
        {
            return services;
        }

        foreach (var (name, keyNode, value) in map.Entries)
        {
            var servicePath = ParseContext.Child(path, name);
            if (Values.CheckIdentifier(name, servicePath, keyNode, context) && Service(name, value, servicePath, context) is { } service)
            {
                services.Add(service);
            }
        }

        return services;
    }

    private static ServiceDefinition? Service(string name, YamlNode node, string path, ParseContext context)
    {
        if (Mapping.From(node, path, context, "a service") is not { } map)
        {
            return null;
        }

        var service = new ServiceDefinition(
            name,
            Values.Choice(map.Take("preset"), ParseContext.Child(path, "preset"), context, Presets),
            Values.ScalarText(map.Take("version"), ParseContext.Child(path, "version"), context),
            Values.NonEmptyText(map.Take("image"), ParseContext.Child(path, "image"), context),
            Collections.VariableMap(map.Take("env"), ParseContext.Child(path, "env"), context) ?? Empty.Map,
            Values.NonEmptyText(map.Take("ready"), ParseContext.Child(path, "ready"), context));
        map.ReportUnknownKeys(context);

        if (map.Has("preset") == map.Has("image"))
        {
            context.Error(DiagnosticCodes.ConflictingKeys, path, node, "A service needs exactly one of 'preset' and 'image'.");
            return null;
        }

        if (map.Has("image") && map.Has("version"))
        {
            context.Error(DiagnosticCodes.ConflictingKeys, ParseContext.Child(path, "version"), node, "'version' belongs to a preset; put the version into the image tag instead.");
            return null;
        }

        return service;
    }

    public static IReadOnlyList<ReportDefinition> Reports(YamlNode? node, string path, ParseContext context)
    {
        var reports = new List<ReportDefinition>();
        if (node is null)
        {
            return reports;
        }

        if (node is not YamlSequenceNode { Children.Count: > 0 } sequence)
        {
            context.Error(DiagnosticCodes.WrongType, path, node, "Expected a non-empty list of reports, each with 'format' and 'path'.");
            return reports;
        }

        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (Report(sequence.Children[i], ParseContext.Item(path, i), context) is { } report)
            {
                reports.Add(report);
            }
        }

        return reports;
    }

    private static ReportDefinition? Report(YamlNode node, string path, ParseContext context)
    {
        if (Mapping.From(node, path, context, "a report") is not { } map)
        {
            return null;
        }

        var formatPath = ParseContext.Child(path, "format");
        var formatNode = map.Require("format", context);
        var format = Values.Text(formatNode, formatPath, context);
        var paths = Collections.TextOrList(map.Require("path", context), ParseContext.Child(path, "path"), context);
        var name = Values.NonEmptyText(map.Take("name"), ParseContext.Child(path, "name"), context);
        map.ReportUnknownKeys(context);

        if (format is not null && !ReportFormats.Contains(format))
        {
            context.Error(DiagnosticCodes.InvalidValue, formatPath, formatNode, $"'{format}' is not a report format. Use one of: junit, trx, cobertura.");
            return null;
        }

        return format is null || paths is null ? null : new ReportDefinition(format, paths, name);
    }

    public static RetryPolicy? Retry(YamlNode? node, string path, ParseContext context)
    {
        if (node is null || Mapping.From(node, path, context, "retry") is not { } map)
        {
            return null;
        }

        var attempts = Values.Integer(map.Require("max_attempts", context), ParseContext.Child(path, "max_attempts"), context, 2, 10);
        var delay = Values.Integer(map.Take("delay_seconds"), ParseContext.Child(path, "delay_seconds"), context, 0, 3600) ?? 0;
        map.ReportUnknownKeys(context);
        return attempts is null ? null : new RetryPolicy(attempts.Value, TimeSpan.FromSeconds(delay));
    }
}
