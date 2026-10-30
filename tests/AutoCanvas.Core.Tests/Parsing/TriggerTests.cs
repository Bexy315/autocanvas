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
    public void Invalid_triggers_are_reported(string triggers, string code, string path) => Rejects(triggers, code, path);

    [Fact]
    public void Triggers_must_be_a_mapping() =>
        SingleError("triggers: manual\n" + MinimalJob, DiagnosticCodes.WrongType, "triggers");
}
