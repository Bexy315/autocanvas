using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using AutoCanvas.Core.Parsing;
using static AutoCanvas.Core.Tests.Parsing.ParserAssertions;

namespace AutoCanvas.Core.Tests.Parsing;

public sealed class TriggerTests
{
    private static Triggers Parse(string triggers) => ParseValid("triggers:\n" + Indent(triggers, "  ") + "\n" + MinimalJob).Triggers;

    private static void Rejects(string triggers, string code, string path) =>
        SingleError("triggers:\n" + Indent(triggers, "  ") + "\n" + MinimalJob, code, path);

    [Fact]
    public void All_trigger_kinds_are_read()
    {
        var triggers = Parse("""
            manual: true
            webhook: true
            push:
              branches: [main, 'release/*']
              tags: ['v*']
              paths: ['src/**']
            schedule:
              - cron: '0 3 * * 1-5'
                timezone: Europe/Berlin
              - interval: 90s
            automation_completed:
              automation: build
              on: always
            """);

        triggers.Manual.ShouldBeTrue();
        triggers.Webhook.ShouldBeTrue();
        triggers.Push.ShouldNotBeNull().Branches.ShouldBe(["main", "release/*"]);
        triggers.Push.Tags.ShouldBe(["v*"]);
        triggers.Push.Paths.ShouldBe(["src/**"]);
        triggers.Schedules.ShouldBe([new CronSchedule("0 3 * * 1-5", "Europe/Berlin"), new IntervalSchedule(TimeSpan.FromSeconds(90))]);
        triggers.AutomationCompleted.ShouldBe(new AutomationCompletedTrigger("build", OutcomeFilter.Always));
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    public void Intervals_have_units(string interval, int seconds) =>
        Parse($"schedule:\n  - interval: {interval}").Schedules.ShouldHaveSingleItem().ShouldBe(new IntervalSchedule(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Push_without_filters_means_all_branches()
    {
        var push = Parse("push: {}").Push.ShouldNotBeNull();

        push.Branches.ShouldBeEmpty();
        push.Tags.ShouldBeEmpty();
        push.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void Pull_requests_are_read_with_their_types_or_the_default_ones()
    {
        var chosen = Parse("""
            pull_request:
              branches: [main]
              paths: ['src/**']
              types: [opened, ready_for_review]
            """).PullRequest.ShouldNotBeNull();
        chosen.Branches.ShouldBe(["main"]);
        chosen.Paths.ShouldBe(["src/**"]);
        chosen.Types.ShouldBe(["opened", "ready_for_review"]);

        var all = Parse("pull_request: {}").PullRequest.ShouldNotBeNull();
        all.Branches.ShouldBeEmpty();
        all.Types.ShouldBe(["opened", "synchronize", "reopened"]);
        Parse("manual: true").PullRequest.ShouldBeNull();
    }

    [Theory]
    [InlineData("opened", "main", null, true)]
    [InlineData("synchronize", "main", "src/a.cs", true)]
    [InlineData("synchronize", "main", "docs/a.md", false)]
    [InlineData("closed", "main", null, false)]
    [InlineData("opened", "develop", null, false)]
    [InlineData("opened", "release/2", null, true)]
    public void Pull_request_triggers_match_type_target_branch_and_paths(string action, string baseBranch, string? changed, bool matches)
    {
        var trigger = new PullRequestTrigger(["main", "release/*"], ["src/**"], PullRequestTrigger.DefaultTypes);

        trigger.Matches(action, baseBranch, changed is null ? null : [changed]).ShouldBe(matches);
    }

    [Fact]
    public void Automation_completed_defaults_to_success() =>
        Parse("automation_completed: { automation: build }").AutomationCompleted.ShouldNotBeNull().On.ShouldBe(OutcomeFilter.Success);

    [Theory]
    [InlineData("{}", DiagnosticCodes.InvalidValue, "triggers")]
    [InlineData("manual: yes", DiagnosticCodes.WrongType, "triggers.manual")]
    [InlineData("push: [main]", DiagnosticCodes.WrongType, "triggers.push")]
    [InlineData("push: { branches: [] }", DiagnosticCodes.InvalidValue, "triggers.push.branches")]
    [InlineData("push: { branches: main }", DiagnosticCodes.WrongType, "triggers.push.branches")]
    [InlineData("push: { branches: [main, main] }", DiagnosticCodes.InvalidValue, "triggers.push.branches[1]")]
    [InlineData("push: { branches: [''] }", DiagnosticCodes.InvalidValue, "triggers.push.branches[0]")]
    [InlineData("push: { branch: [main] }", DiagnosticCodes.UnknownKey, "triggers.push.branch")]
    [InlineData("schedule: []", DiagnosticCodes.WrongType, "triggers.schedule")]
    [InlineData("schedule: [ '0 3 * * *' ]", DiagnosticCodes.WrongType, "triggers.schedule[0]")]
    [InlineData("schedule: [ { cron: '0 3 * * *', interval: 5m } ]", DiagnosticCodes.ConflictingKeys, "triggers.schedule[0]")]
    [InlineData("schedule: [ { timezone: UTC } ]", DiagnosticCodes.ConflictingKeys, "triggers.schedule[0]")]
    [InlineData("schedule: [ { cron: '0 3 * *' } ]", DiagnosticCodes.InvalidValue, "triggers.schedule[0].cron")]
    [InlineData("schedule: [ { cron: '' } ]", DiagnosticCodes.InvalidValue, "triggers.schedule[0].cron")]
    [InlineData("schedule: [ { interval: 5 } ]", DiagnosticCodes.WrongType, "triggers.schedule[0].interval")]
    [InlineData("schedule: [ { interval: 5w } ]", DiagnosticCodes.InvalidValue, "triggers.schedule[0].interval")]
    [InlineData("schedule: [ { interval: 0m } ]", DiagnosticCodes.InvalidValue, "triggers.schedule[0].interval")]
    [InlineData("schedule: [ { interval: 9s } ]", DiagnosticCodes.InvalidValue, "triggers.schedule[0].interval")]
    [InlineData("schedule: [ { interval: 5m, timezone: UTC } ]", DiagnosticCodes.UnknownKey, "triggers.schedule[0].timezone")]
    [InlineData("automation_completed: { on: failure }", DiagnosticCodes.MissingKey, "triggers.automation_completed")]
    [InlineData("automation_completed: { automation: a, on: sometimes }", DiagnosticCodes.InvalidValue, "triggers.automation_completed.on")]
    [InlineData("automation_completed: build", DiagnosticCodes.WrongType, "triggers.automation_completed")]
    [InlineData("on_push: {}", DiagnosticCodes.UnknownKey, "triggers.on_push")]
    [InlineData("pull_request: { types: [opened, merged] }", DiagnosticCodes.InvalidValue, "triggers.pull_request.types")]
    [InlineData("pull_request: { types: [] }", DiagnosticCodes.InvalidValue, "triggers.pull_request.types")]
    [InlineData("pull_request: { base: main }", DiagnosticCodes.UnknownKey, "triggers.pull_request.base")]
    [InlineData("pull_request: main", DiagnosticCodes.WrongType, "triggers.pull_request")]
    public void Invalid_triggers_are_reported(string triggers, string code, string path) => Rejects(triggers, code, path);

    [Fact]
    public void Triggers_must_be_a_mapping() =>
        SingleError("triggers: manual\n" + MinimalJob, DiagnosticCodes.WrongType, "triggers");
}
