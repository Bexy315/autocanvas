using System.Globalization;
using System.Text.RegularExpressions;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Reads the <c>triggers</c> block.</summary>
internal static partial class TriggersReader
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);

    private static readonly Dictionary<string, OutcomeFilter> Outcomes = new(StringComparer.Ordinal)
    {
        ["success"] = OutcomeFilter.Success,
        ["failure"] = OutcomeFilter.Failure,
        ["always"] = OutcomeFilter.Always,
    };

    public static IReadOnlyDictionary<string, OutcomeFilter> OutcomeNames => Outcomes;

    public static Triggers Read(YamlNode? node, string path, ParseContext context)
    {
        if (node is null || Mapping.From(node, path, context, "triggers") is not { } map)
        {
            return Triggers.None;
        }

        if (!map.Node.Children.Any())
        {
            context.Error(DiagnosticCodes.InvalidValue, path, node, "Add at least one trigger, or leave 'triggers' out.");
        }

        var triggers = new Triggers(
            Manual: Values.Boolean(map.Take("manual"), ParseContext.Child(path, "manual"), context) ?? false,
            Push: ReadPush(map.Take("push"), ParseContext.Child(path, "push"), context),
            Schedules: ReadSchedules(map.Take("schedule"), ParseContext.Child(path, "schedule"), context),
            Webhook: Values.Boolean(map.Take("webhook"), ParseContext.Child(path, "webhook"), context) ?? false,
            AutomationCompleted: ReadAutomationCompleted(map.Take("automation_completed"), ParseContext.Child(path, "automation_completed"), context));
        map.ReportUnknownKeys(context);
        return triggers;
    }

    private static PushTrigger? ReadPush(YamlNode? node, string path, ParseContext context)
    {
        if (node is null || Mapping.From(node, path, context, "the push trigger") is not { } map)
        {
            return null;
        }

        var push = new PushTrigger(
            Collections.TextList(map.Take("branches"), ParseContext.Child(path, "branches"), context) ?? [],
            Collections.TextList(map.Take("tags"), ParseContext.Child(path, "tags"), context) ?? [],
            Collections.TextList(map.Take("paths"), ParseContext.Child(path, "paths"), context) ?? []);
        map.ReportUnknownKeys(context);
        return push;
    }

    private static List<ScheduleTrigger> ReadSchedules(YamlNode? node, string path, ParseContext context)
    {
        var result = new List<ScheduleTrigger>();
        if (node is null)
        {
            return result;
        }

        if (node is not YamlSequenceNode { Children.Count: > 0 } sequence)
        {
            context.Error(DiagnosticCodes.WrongType, path, node, "Expected a non-empty list of schedules, each with 'cron' or 'interval'.");
            return result;
        }

        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (ReadSchedule(sequence.Children[i], ParseContext.Item(path, i), context) is { } schedule)
            {
                result.Add(schedule);
            }
        }

        return result;
    }

    private static ScheduleTrigger? ReadSchedule(YamlNode node, string path, ParseContext context)
    {
        if (Mapping.From(node, path, context, "a schedule") is not { } map)
        {
            return null;
        }

        ScheduleTrigger? schedule = null;
        if (map.Has("cron") == map.Has("interval"))
        {
            context.Error(DiagnosticCodes.ConflictingKeys, path, node, "A schedule needs exactly one of 'cron' and 'interval'.");

            // Already reported; do not add noise about the keys involved.
            map.Take("cron");
            map.Take("interval");
            map.Take("timezone");
        }
        else if (map.Has("cron"))
        {
            schedule = ReadCron(map, path, context);
        }
        else
        {
            // 'timezone' stays untaken here: it only applies to cron and is reported as unknown.
            schedule = ReadInterval(map.Take("interval"), ParseContext.Child(path, "interval"), context);
        }

        map.ReportUnknownKeys(context);
        return schedule;
    }

    private static CronSchedule? ReadCron(Mapping map, string path, ParseContext context)
    {
        var cronPath = ParseContext.Child(path, "cron");
        var cronNode = map.Take("cron");
        var expression = Values.NonEmptyText(cronNode, cronPath, context);
        var timeZone = Values.NonEmptyText(map.Take("timezone"), ParseContext.Child(path, "timezone"), context);
        if (expression is null)
        {
            return null;
        }

        if (expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5)
        {
            context.Error(DiagnosticCodes.InvalidValue, cronPath, cronNode, $"'{expression}' is not a five-field cron expression (minute hour day-of-month month day-of-week).");
            return null;
        }

        return new CronSchedule(expression, timeZone);
    }

    private static IntervalSchedule? ReadInterval(YamlNode? node, string path, ParseContext context)
    {
        var text = Values.NonEmptyText(node, path, context);
        if (text is null)
        {
            return null;
        }

        var match = IntervalPattern().Match(text);
        if (!match.Success || !long.TryParse(match.Groups["amount"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            context.Error(DiagnosticCodes.InvalidValue, path, node, $"'{text}' is not an interval. Write a number and a unit: 30s, 5m, 2h or 1d.");
            return null;
        }

        var interval = match.Groups["unit"].Value switch
        {
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            "h" => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromDays(amount),
        };

        if (interval < MinimumInterval)
        {
            context.Error(DiagnosticCodes.InvalidValue, path, node, "The interval must be at least 10s.");
            return null;
        }

        return new IntervalSchedule(interval);
    }

    private static AutomationCompletedTrigger? ReadAutomationCompleted(YamlNode? node, string path, ParseContext context)
    {
        if (node is null || Mapping.From(node, path, context, "the automation_completed trigger") is not { } map)
        {
            return null;
        }

        var automation = Values.NonEmptyText(map.Require("automation", context), ParseContext.Child(path, "automation"), context);
        var on = Values.Choice(map.Take("on"), ParseContext.Child(path, "on"), context, Outcomes) ?? OutcomeFilter.Success;
        map.ReportUnknownKeys(context);
        return automation is null ? null : new AutomationCompletedTrigger(automation, on);
    }

    [GeneratedRegex("^(?<amount>[1-9][0-9]{0,6})(?<unit>[smhd])$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IntervalPattern();
}
