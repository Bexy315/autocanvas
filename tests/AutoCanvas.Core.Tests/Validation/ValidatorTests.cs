using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Parsing;
using AutoCanvas.Core.Validation;

namespace AutoCanvas.Core.Tests.Validation;

public sealed class ValidatorTests
{
    private static IReadOnlyList<Diagnostic> Validate(string yaml)
    {
        var parsed = AutomationParser.Parse(yaml);
        parsed.Diagnostics.ShouldBeEmpty(string.Join(Environment.NewLine, parsed.Diagnostics));
        return AutomationValidator.Validate(parsed.Definition!, parsed.Locations);
    }

    private static Diagnostic SingleProblem(string yaml, string code, string path)
    {
        var problems = Validate(yaml);
        var problem = problems.ShouldHaveSingleItem(string.Join(Environment.NewLine, problems));
        problem.Code.ShouldBe(code);
        problem.Path.ShouldBe(path);
        problem.Severity.ShouldBe(DiagnosticSeverity.Error);
        return problem;
    }

    [Fact]
    public void Runs_started_by_schedules_and_other_automations_read_their_trigger() =>
        Validate("""
            version: 1
            jobs:
              report:
                steps:
                  - run: echo ${{ trigger.schedule }} ${{ trigger.scheduled_at }} ${{ trigger.automation }} ${{ trigger.run_id }} ${{ trigger.run_number }} ${{ trigger.result }} ${{ trigger.body.repository.name }}
            """).ShouldBeEmpty();

    [Fact]
    public void A_coherent_automation_has_no_problems() =>
        Validate("""
            version: 1
            variables: { TAG: '${{ run.commit_short_sha }}' }
            changed_when: ${{ jobs.build.outputs.changed == 'true' }}
            jobs:
              build:
                if: trigger.event == 'push'
                env: { MODE: '${{ vars.MODE }}', TOKEN: '${{ secrets.TOKEN }}' }
                steps:
                  - id: compile
                    run: make TAG=${{ vars.TAG }}
                  - id: call
                    type: http_request
                    if: ${{ steps.compile.outputs.ok }}
                    with:
                      url: https://example.org/${{ trigger.headers.X-Id }}
                      items: [ '${{ steps.compile.outputs.version }}', 3 ]
                    outputs: { code: '${{ steps.call.outputs.status }}' }
                outputs:
                  changed: ${{ steps.call.outputs.code }}
              route:
                type: switch
                needs: [build]
                expression: ${{ jobs.build.outputs.changed }}
                cases:
                  - { name: deploy, when: 'true' }
                  - { name: skip, default: true }
              deploy:
                needs: [{ job: route, case: deploy }]
                image: app:${{ jobs.build.outputs.changed }}
                steps:
                  - run: echo ${{ jobs.route.result }} ${{ trigger.query.ref }}
              report:
                finally: true
                steps:
                  - run: echo ${{ jobs.deploy.result }} ${{ run.status }}
            """).ShouldBeEmpty();

    [Fact]
    public void Needs_must_name_existing_jobs()
    {
        var problem = SingleProblem("""
            version: 1
            jobs:
              build: { steps: [ { run: a } ] }
              test: { needs: [biuld], steps: [ { run: a } ] }
            """, DiagnosticCodes.UnknownJob, "jobs.test.needs[0]");

        problem.Message.ShouldContain("Did you mean 'build'?");
        problem.Location.ShouldBe(new SourceLocation(4, 19));
    }

    [Fact]
    public void Unknown_jobs_get_no_suggestion_when_nothing_is_close() =>
        SingleProblem("version: 1\njobs:\n  build: { needs: [deploy], steps: [ { run: a } ] }\n", DiagnosticCodes.UnknownJob, "jobs.build.needs[0]")
            .Message.ShouldNotContain("Did you mean");

    [Fact]
    public void A_job_cannot_need_the_same_job_twice() =>
        SingleProblem("""
            version: 1
            jobs:
              a: { steps: [ { run: a } ] }
              b: { needs: [a, { job: a, on: always }], steps: [ { run: a } ] }
            """, DiagnosticCodes.DuplicateNeed, "jobs.b.needs[1]");

    [Fact]
    public void Cycles_are_reported_once_with_their_jobs()
    {
        var problem = SingleProblem("""
            version: 1
            jobs:
              a: { needs: [c], steps: [ { run: a } ] }
              b: { needs: [a], steps: [ { run: a } ] }
              c: { needs: [b], steps: [ { run: a } ] }
            """, DiagnosticCodes.DependencyCycle, "jobs.a.needs");

        problem.Message.ShouldContain("a → c → b → a");
    }

    [Fact]
    public void A_job_cannot_need_itself() =>
        SingleProblem("version: 1\njobs:\n  a: { needs: [a], steps: [ { run: a } ] }\n", DiagnosticCodes.DependencyCycle, "jobs.a.needs");

    [Fact]
    public void Expressions_in_a_cycle_are_still_checked_without_reachability_noise()
    {
        var problems = Validate("""
            version: 1
            jobs:
              a: { needs: [b], steps: [ { run: '${{ jobs.b.result }} ${{ nope.x }}' } ] }
              b: { needs: [a], steps: [ { run: a } ] }
            """);

        problems.Select(p => p.Code).ShouldBe([DiagnosticCodes.DependencyCycle, DiagnosticCodes.UnknownReference], ignoreOrder: true);
    }

    [Fact]
    public void No_job_can_wait_for_a_finally_job() =>
        SingleProblem("""
            version: 1
            jobs:
              cleanup: { finally: true, steps: [ { run: a } ] }
              b: { needs: [cleanup], steps: [ { run: a } ] }
            """, DiagnosticCodes.NeedsFinallyJob, "jobs.b.needs[0]");

    [Theory]
    [InlineData("a", "is not a switch")]
    [InlineData("route", "has no case 'prod'")]
    public void Cases_must_exist_on_a_switch(string target, string message) =>
        SingleProblem($$"""
            version: 1
            jobs:
              a: { steps: [ { run: a } ] }
              route: { type: switch, expression: x, cases: [ { name: other, default: true } ] }
              b: { needs: [ { job: {{target}}, case: prod } ], steps: [ { run: a } ] }
            """, DiagnosticCodes.UnknownCase, "jobs.b.needs[0]").Message.ShouldContain(message);

    [Fact]
    public void Switch_cases_must_be_unique_with_one_default()
    {
        var problems = Validate("""
            version: 1
            jobs:
              route:
                type: switch
                expression: x
                cases:
                  - { name: a, when: '1' }
                  - { name: a, when: '2' }
                  - { name: b, default: true }
                  - { name: c, default: true }
            """);

        problems.Select(p => (p.Code, p.Path)).ShouldBe([
            (DiagnosticCodes.InvalidCases, "jobs.route.cases[1].name"),
            (DiagnosticCodes.InvalidCases, "jobs.route.cases[3]"),
        ]);
    }

    [Fact]
    public void Step_ids_must_be_unique_within_a_job() =>
        SingleProblem("""
            version: 1
            jobs:
              a:
                steps:
                  - { id: x, run: a }
                  - { id: x, run: b }
            """, DiagnosticCodes.DuplicateStepId, "jobs.a.steps[1].id");

    [Theory]
    [InlineData("${{ nope }}", DiagnosticCodes.UnknownReference, "nothing an expression can read")]
    [InlineData("${{ vars }}", DiagnosticCodes.UnknownReference, "vars.NAME")]
    [InlineData("${{ secrets.A.B }}", DiagnosticCodes.UnknownReference, "secrets.NAME")]
    [InlineData("${{ trigger.nothing }}", DiagnosticCodes.UnknownReference, "trigger.branch")]
    [InlineData("${{ trigger.headers }}", DiagnosticCodes.UnknownReference, "trigger.headers.NAME")]
    [InlineData("${{ run.pipeline_name }}", DiagnosticCodes.UnknownReference, "run.automation_name")]
    [InlineData("${{ jobs.a }}", DiagnosticCodes.UnknownReference, "jobs.JOB.outputs.NAME or jobs.JOB.result")]
    [InlineData("${{ jobs.zzz.result }}", DiagnosticCodes.UnknownReference, "does not exist")]
    [InlineData("${{ jobs.a.outputs.missing }}", DiagnosticCodes.UnknownReference, "does not publish")]
    [InlineData("${{ jobs.other.result }}", DiagnosticCodes.UnreachableReference, "Add 'other' to needs")]
    [InlineData("${{ steps.first }}", DiagnosticCodes.UnknownReference, "steps.STEP.outputs.NAME")]
    [InlineData("${{ steps.later.outputs.x }}", DiagnosticCodes.UnreachableReference, "not an earlier step")]
    [InlineData("${{ steps.second.outputs.x }}", DiagnosticCodes.UnreachableReference, "not an earlier step")]
    [InlineData("${{ vars.A ==  }}", DiagnosticCodes.ExpressionSyntax, "ends too early")]
    [InlineData("${{ vars.A", DiagnosticCodes.ExpressionSyntax, "not closed")]
    public void Expressions_in_steps_are_checked(string text, string code, string message) =>
        SingleProblem($$"""
            version: 1
            jobs:
              a: { outputs: { built: x }, steps: [ { run: a } ] }
              other: { steps: [ { run: a } ] }
              b:
                needs: [a]
                steps:
                  - { id: first, run: a }
                  - { id: second, run: '{{text}}' }
                  - { id: later, run: a }
            """, code, "jobs.b.steps[1].run").Message.ShouldContain(message);

    [Fact]
    public void Jobs_of_switch_and_join_have_no_outputs() =>
        SingleProblem("""
            version: 1
            jobs:
              j: { type: join, needs: [a] }
              a: { steps: [ { run: a } ] }
              b: { needs: [j], steps: [ { run: '${{ jobs.j.outputs.x }}' } ] }
            """, DiagnosticCodes.UnknownReference, "jobs.b.steps[0].run");

    [Fact]
    public void Indirect_needs_make_jobs_readable() =>
        Validate("""
            version: 1
            jobs:
              a: { steps: [ { run: a } ] }
              b: { needs: [a], steps: [ { run: a } ] }
              c: { needs: [b], steps: [ { run: '${{ jobs.a.result }}' } ] }
            """).ShouldBeEmpty();

    [Fact]
    public void Variables_can_only_read_trigger_and_run()
    {
        var problems = Validate("""
            version: 1
            variables: { A: '${{ vars.B }}', B: '${{ jobs.a.result }}', C: '${{ steps.x.outputs.y }}' }
            jobs:
              a: { steps: [ { run: a } ] }
            """);

        problems.Select(p => p.Path).ShouldBe(["variables.A", "variables.B", "variables.C"]);
        problems.ShouldAllBe(p => p.Code == DiagnosticCodes.UnknownReference);
    }

    [Fact]
    public void Job_level_values_cannot_read_steps() =>
        SingleProblem("""
            version: 1
            jobs:
              a: { if: steps.x.outputs.y, steps: [ { id: x, run: a } ] }
            """, DiagnosticCodes.UnknownReference, "jobs.a.if").Message.ShouldContain("only visible inside their own job");

    [Theory]
    [InlineData("image: '${{ nope }}'", "jobs.a.image")]
    [InlineData("runs_on: ['${{ nope }}']", "jobs.a.runs_on[0]")]
    [InlineData("env: { X: '${{ nope }}' }", "jobs.a.env.X")]
    [InlineData("checkout: [ { repository: '${{ nope }}' } ]", "jobs.a.checkout[0].repository")]
    [InlineData("checkout: [ { repository: x, ref: '${{ nope }}' } ]", "jobs.a.checkout[0].ref")]
    [InlineData("checkout: [ { repository: x, path: '${{ nope }}' } ]", "jobs.a.checkout[0].path")]
    [InlineData("services: { db: { image: '${{ nope }}' } }", "jobs.a.services.db.image")]
    [InlineData("services: { db: { preset: redis, ready: '${{ nope }}' } }", "jobs.a.services.db.ready")]
    [InlineData("services: { db: { preset: redis, env: { P: '${{ nope }}' } } }", "jobs.a.services.db.env.P")]
    [InlineData("outputs: { o: '${{ nope }}' }", "jobs.a.outputs.o")]
    [InlineData("reports: [ { format: junit, path: '${{ nope }}' } ]", "jobs.a.reports[0].path")]
    [InlineData("reports: [ { format: junit, path: [a, '${{ nope }}'] } ]", "jobs.a.reports[0].path[1]")]
    [InlineData("reports: [ { format: junit, path: a, name: '${{ nope }}' } ]", "jobs.a.reports[0].name")]
    public void Every_job_value_is_checked(string line, string path) =>
        SingleProblem($"version: 1\njobs:\n  a:\n    {line}\n    steps: [ {{ run: a }} ]\n", DiagnosticCodes.UnknownReference, path);

    [Theory]
    [InlineData("{ run: a, if: '${{ nope }}' }", "jobs.a.steps[0].if")]
    [InlineData("{ run: a, env: { X: '${{ nope }}' } }", "jobs.a.steps[0].env.X")]
    [InlineData("{ run: a, working_directory: '${{ nope }}' }", "jobs.a.steps[0].working_directory")]
    [InlineData("{ type: n, with: { a: { b: [x, '${{ nope }}'] } } }", "jobs.a.steps[0].with.a.b[1]")]
    [InlineData("{ type: n, outputs: { o: '${{ nope }}' } }", "jobs.a.steps[0].outputs.o")]
    [InlineData("{ type: n, outputs: { o: '${{ steps.self.outputs.x }}' } }", "jobs.a.steps[0].outputs.o")]
    public void Every_step_value_is_checked(string step, string path) =>
        SingleProblem($"version: 1\njobs:\n  a:\n    steps: [ {step} ]\n", path.EndsWith(".o", StringComparison.Ordinal) && step.Contains("self", StringComparison.Ordinal) ? DiagnosticCodes.UnreachableReference : DiagnosticCodes.UnknownReference, path);

    [Fact]
    public void Switch_expressions_and_conditions_are_checked()
    {
        var problems = Validate("""
            version: 1
            changed_when: ${{ a && }}
            jobs:
              route: { type: switch, if: '${{ x }} ${{ y }}', expression: '${{ nope.z }}', cases: [ { name: a, default: true } ] }
            """);

        problems.Select(p => (p.Code, p.Path)).ShouldBe([
            (DiagnosticCodes.ExpressionSyntax, "changed_when"),
            (DiagnosticCodes.ExpressionSyntax, "jobs.route.if"),
            (DiagnosticCodes.UnknownReference, "jobs.route.expression"),
        ]);
    }

    [Fact]
    public void Without_locations_problems_have_no_position()
    {
        var parsed = AutomationParser.Parse("version: 1\njobs:\n  a: { needs: [b], steps: [ { run: a } ] }\n");

        AutomationValidator.Validate(parsed.Definition!).ShouldHaveSingleItem().Location.ShouldBeNull();
    }

    [Fact]
    public void Validate_rejects_null() =>
        Should.Throw<ArgumentNullException>(() => AutomationValidator.Validate(null!));
}
