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
    [InlineData("refs/heads/feature/x", "feature/x")]
    [InlineData("refs/tags/v1", null)]
    [InlineData(null, null)]
    public void A_push_run_reads_its_event_branch_and_commit(string? gitRef, string? branch)
    {
        var run = new Run
        {
            Id = Guid.CreateVersion7(),
            FolderId = Guid.CreateVersion7(),
            AutomationName = "build",
            AutomationVersion = 1,
            Number = 1,
            Yaml = "version: 1",
            Trigger = RunTriggerKind.Push,
            TriggeredByName = "ada",
            CommitSha = gitRef is null ? null : "0123456789abcdef0123456789abcdef01234567",
            Ref = gitRef,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        var values = new RunValues(run, new ResolvedConfiguration(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)), "success");

        Resolve(values, "trigger.event").ShouldBe("push");
        Resolve(values, "run.branch").ShouldBe(branch);
        Resolve(values, "run.commit_sha").ShouldBe(run.CommitSha);
        Resolve(values, "run.commit_short_sha").ShouldBe(run.CommitSha?[..7]);
    }

    private static string? Resolve(RunValues values, string path)
    {
        values.TryResolve(path.Split('.'), out var value).ShouldBeTrue();
        return value;
    }

    [Theory]
    [InlineData("trigger.Body")]
    [InlineData("trigger.headers.missing")]
    [InlineData("trigger.body.deeper")]
    [InlineData("run.unknown")]
    [InlineData("vars.MISSING")]
    [InlineData("steps.build.result")]
    public void Unknown_paths_do_not_resolve(string path) =>
        Values.TryResolve(path.Split('.'), out _).ShouldBeFalse();

    [Theory]
    [InlineData("jobs.image.outputs.digest", "sha256:1")]
    [InlineData("jobs.image.outputs.missing", null)]
    [InlineData("jobs.other.outputs.digest", null)]
    [InlineData("steps.build.outputs.tag", "v2")]
    [InlineData("steps.later.outputs.tag", null)]
    public void Outputs_of_jobs_and_earlier_steps_resolve_and_unset_ones_are_empty(string path, string? expected)
    {
        var jobs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["image"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["digest"] = "sha256:1" },
        };
        var steps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["build"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["tag"] = "v2" },
        };
        var values = new RunValues(Webhook, new ResolvedConfiguration(new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal)), "success", jobs).WithSteps(steps);

        values.TryResolve(path.Split('.'), out var value).ShouldBeTrue();

        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(RunTriggerKind.Manual, "manual")]
    [InlineData(RunTriggerKind.Schedule, "schedule")]
    [InlineData(RunTriggerKind.Webhook, "webhook")]
    [InlineData(RunTriggerKind.AutomationCompleted, "automation_completed")]
    public void Trigger_events_have_their_documented_names(RunTriggerKind kind, string name) =>
        RunValues.TriggerEvent(kind).ShouldBe(name);
}
