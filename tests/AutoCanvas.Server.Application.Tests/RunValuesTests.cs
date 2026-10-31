using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Application.Tests;

public sealed class RunValuesTests
{
    private static readonly Run Webhook = new()
    {
        Id = Guid.CreateVersion7(),
        FolderId = Guid.CreateVersion7(),
        AutomationName = "hook",
        AutomationVersion = 1,
        Number = 7,
        Yaml = "version: 1",
        Trigger = RunTriggerKind.Webhook,
        TriggerData = """{"body":"{\"a\":1}","headers":{"X-Event":"push"},"query":{"ref":"main"},"count":3,"none":null}""",
        TriggeredByName = "webhook",
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static readonly RunValues Values = new(
        Webhook,
        new ResolvedConfiguration(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["REGION"] = "eu" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TOKEN"] = "t0ken" }),
        "success");

    [Theory]
    [InlineData("trigger.event", "webhook")]
    [InlineData("trigger.body", "{\"a\":1}")]
    [InlineData("trigger.headers.x-event", "push")]
    [InlineData("trigger.query.ref", "main")]
    [InlineData("trigger.count", "3")]
    [InlineData("trigger.none", null)]
    [InlineData("run.automation_name", "hook")]
    [InlineData("run.triggered_by", "webhook")]
    [InlineData("run.status", "success")]
    [InlineData("vars.region", "eu")]
    [InlineData("secrets.TOKEN", "t0ken")]
    public void Known_paths_resolve(string path, string? expected)
    {
        Values.TryResolve(path.Split('.'), out var value).ShouldBeTrue();

        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("trigger.Body")]
    [InlineData("trigger.headers.missing")]
    [InlineData("trigger.body.deeper")]
    [InlineData("run.unknown")]
    [InlineData("vars.MISSING")]
    [InlineData("steps.build.outputs.x")]
    public void Unknown_paths_do_not_resolve(string path) =>
        Values.TryResolve(path.Split('.'), out _).ShouldBeFalse();

    [Theory]
    [InlineData(RunTriggerKind.Manual, "manual")]
    [InlineData(RunTriggerKind.Schedule, "schedule")]
    [InlineData(RunTriggerKind.Webhook, "webhook")]
    [InlineData(RunTriggerKind.AutomationCompleted, "automation_completed")]
    public void Trigger_events_have_their_documented_names(RunTriggerKind kind, string name) =>
        RunValues.TriggerEvent(kind).ShouldBe(name);
}
