using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using AutoCanvas.Core.Parsing;
using static AutoCanvas.Core.Tests.Parsing.ParserAssertions;

namespace AutoCanvas.Core.Tests.Parsing;

public sealed class JobTests
{
    [Fact]
    public void A_step_job_with_everything_is_read()
    {
        var job = ParseJob("""
            name: Build it
            needs: [lint, { job: test, on: always }]
            if: ${{ trigger.branch == 'main' }}
            runs_on: [linux, docker]
            image: alpine:3.20
            checkout:
              - repository: self
              - repository: https://example.org/tools.git
                ref: v2
                path: tools
                depth: 0
            env: { MODE: release }
            services:
              db: { preset: postgres, version: 17, env: { POSTGRES_PASSWORD: x } }
              cache: { image: valkey/valkey:8, ready: valkey-cli ping }
            steps:
              - run: make
            outputs: { digest: '${{ steps.build.outputs.digest }}' }
            reports:
              - { format: junit, path: '**/TEST-*.xml', name: Unit }
              - { format: cobertura, path: [a.xml, b.xml] }
            timeout_minutes: 20
            retry: { max_attempts: 3, delay_seconds: 15 }
            """);

        job.Name.ShouldBe("Build it");
        job.Needs.ShouldBe([new JobNeed("lint", OutcomeFilter.Success, null), new JobNeed("test", OutcomeFilter.Always, null)]);
        job.Condition.ShouldBe("${{ trigger.branch == 'main' }}");
        job.RunsOn.ShouldBe(["linux", "docker"]);
        job.Image.ShouldBe("alpine:3.20");
        job.Checkout.Kind.ShouldBe(CheckoutKind.Explicit);
        job.Checkout.Repositories.ShouldBe([new CheckoutRepository("self", null, null, null), new CheckoutRepository("https://example.org/tools.git", "v2", "tools", 0)]);
        job.Env["MODE"].ShouldBe("release");
        job.Services.Select(s => (s.Name, s.Preset, s.Version, s.Image, s.Ready)).ShouldBe([("db", ServicePreset.Postgres, "17", null, null), ("cache", null, null, "valkey/valkey:8", "valkey-cli ping")]);
        job.Services[0].Env["POSTGRES_PASSWORD"].ShouldBe("x");
        job.Outputs["digest"].ShouldBe("${{ steps.build.outputs.digest }}");
        job.Reports.Select(r => (r.Format, string.Join(',', r.Paths), r.Name)).ShouldBe([("junit", "**/TEST-*.xml", "Unit"), ("cobertura", "a.xml,b.xml", null)]);
        job.TimeoutMinutes.ShouldBe(20);
        job.Retry.ShouldBe(new RetryPolicy(3, TimeSpan.FromSeconds(15)));
        job.RunsFinally.ShouldBeFalse();
    }

    [Fact]
    public void A_job_without_optional_keys_gets_defaults()
    {
        var job = ParseJob("steps:\n  - run: make");

        job.Name.ShouldBeNull();
        job.Needs.ShouldBeEmpty();
        job.RunsOn.ShouldBeEmpty();
        job.Checkout.ShouldBe(CheckoutSetting.Default);
        job.Services.ShouldBeEmpty();
        job.Outputs.ShouldBeEmpty();
        job.Reports.ShouldBeEmpty();
        job.Retry.ShouldBeNull();
    }

    [Fact]
    public void Runs_on_accepts_a_single_label() =>
        ParseJob("runs_on: linux\nsteps:\n  - run: make").RunsOn.ShouldBe(["linux"]);

    [Fact]
    public void Checkout_false_disables_the_implicit_checkout() =>
        ParseJob("checkout: false\nsteps:\n  - run: make").Checkout.ShouldBe(CheckoutSetting.Disabled);

    [Fact]
    public void Finally_jobs_may_also_run_on_cancel()
    {
        var job = ParseJob("finally: true\non_cancel: true\nsteps:\n  - run: report");

        job.RunsFinally.ShouldBeTrue();
        job.RunsOnCancel.ShouldBeTrue();
    }

    [Fact]
    public void Switch_and_join_jobs_are_read()
    {
        var automation = ParseValid("""
            version: 1
            jobs:
              route:
                type: switch
                expression: ${{ trigger.branch }}
                cases:
                  - { name: prod, when: main }
                  - { name: flag, when: true }
                  - { name: other, default: true }
              deploy:
                needs: [{ job: route, case: prod }]
                steps: [ { run: deploy } ]
              done:
                type: join
                name: Done
                needs: [deploy]
            """);

        var route = automation.Jobs[0].ShouldBeOfType<SwitchJob>();
        route.Expression.ShouldBe("${{ trigger.branch }}");
        route.Cases.ShouldBe([new SwitchCase("prod", "main", false), new SwitchCase("flag", "true", false), new SwitchCase("other", null, true)]);
        automation.Jobs[1].Needs.ShouldBe([new JobNeed("route", OutcomeFilter.Success, "prod")]);
        var done = automation.Jobs[2].ShouldBeOfType<JoinJob>();
        done.Name.ShouldBe("Done");
        done.Needs.ShouldHaveSingleItem().Job.ShouldBe("deploy");
    }

    [Theory]
    [InlineData("type: matrix\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.type")]
    [InlineData("needs: build\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.needs")]
    [InlineData("needs: []\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.needs")]
    [InlineData("needs: [Build]\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.needs[0]")]
    [InlineData("needs: [[a]]\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.needs[0]")]
    [InlineData("needs: [ { on: always } ]\nsteps: [ { run: a } ]", DiagnosticCodes.MissingKey, "jobs.build.needs[0]")]
    [InlineData("needs: [ { job: a, on: later } ]\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.needs[0].on")]
    [InlineData("needs: [ { job: a, on: always, case: x } ]\nsteps: [ { run: a } ]", DiagnosticCodes.ConflictingKeys, "jobs.build.needs[0]")]
    [InlineData("needs: [ { job: a, when: x } ]\nsteps: [ { run: a } ]", DiagnosticCodes.UnknownKey, "jobs.build.needs[0].when")]
    [InlineData("name: ''\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.name")]
    [InlineData("runs_on: []\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.runs_on")]
    [InlineData("checkout: true\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.checkout")]
    [InlineData("checkout: []\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.checkout")]
    [InlineData("checkout: [ self ]\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.checkout[0]")]
    [InlineData("checkout: [ { ref: main } ]\nsteps: [ { run: a } ]", DiagnosticCodes.MissingKey, "jobs.build.checkout[0]")]
    [InlineData("checkout: [ { repository: self, depth: -1 } ]\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.checkout[0].depth")]
    [InlineData("services: [db]\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.services")]
    [InlineData("services: { Db: { preset: redis } }\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.services.Db")]
    [InlineData("services: { db: redis }\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.services.db")]
    [InlineData("services: { db: { preset: mongo } }\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.services.db.preset")]
    [InlineData("services: { db: { env: {} } }\nsteps: [ { run: a } ]", DiagnosticCodes.ConflictingKeys, "jobs.build.services.db")]
    [InlineData("services: { db: { preset: redis, image: redis:7 } }\nsteps: [ { run: a } ]", DiagnosticCodes.ConflictingKeys, "jobs.build.services.db")]
    [InlineData("services: { db: { image: redis:7, version: 7 } }\nsteps: [ { run: a } ]", DiagnosticCodes.ConflictingKeys, "jobs.build.services.db.version")]
    [InlineData("services: { db: { preset: redis, port: 6379 } }\nsteps: [ { run: a } ]", DiagnosticCodes.UnknownKey, "jobs.build.services.db.port")]
    [InlineData("outputs: { Digest: x }\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.outputs.Digest")]
    [InlineData("outputs: [x]\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.outputs")]
    [InlineData("reports: { format: junit }\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.reports")]
    [InlineData("reports: [ junit ]\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.reports[0]")]
    [InlineData("reports: [ { format: sarif, path: a } ]\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.reports[0].format")]
    [InlineData("reports: [ { format: junit } ]\nsteps: [ { run: a } ]", DiagnosticCodes.MissingKey, "jobs.build.reports[0]")]
    [InlineData("retry: 3\nsteps: [ { run: a } ]", DiagnosticCodes.WrongType, "jobs.build.retry")]
    [InlineData("retry: { max_attempts: 1 }\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.retry.max_attempts")]
    [InlineData("retry: { max_attempts: 3, delay_seconds: 3601 }\nsteps: [ { run: a } ]", DiagnosticCodes.InvalidValue, "jobs.build.retry.delay_seconds")]
    [InlineData("retry: { delay_seconds: 5 }\nsteps: [ { run: a } ]", DiagnosticCodes.MissingKey, "jobs.build.retry")]
    [InlineData("finally: true\nneeds: [a]\nsteps: [ { run: a } ]", DiagnosticCodes.ConflictingKeys, "jobs.build.needs")]
    [InlineData("on_cancel: true\nsteps: [ { run: a } ]", DiagnosticCodes.ConflictingKeys, "jobs.build.on_cancel")]
    [InlineData("runs_on: linux", DiagnosticCodes.MissingKey, "jobs.build")]
    [InlineData("steps: []", DiagnosticCodes.WrongType, "jobs.build.steps")]
    [InlineData("steps: { run: a }", DiagnosticCodes.WrongType, "jobs.build.steps")]
    [InlineData("depends_on: [a]\nsteps: [ { run: a } ]", DiagnosticCodes.UnknownKey, "jobs.build.depends_on")]
    public void Invalid_step_jobs_are_reported(string job, string code, string path) =>
        SingleError(WithJob(job), code, path);

    [Theory]
    [InlineData("type: switch\ncases: [ { name: a, default: true } ]", DiagnosticCodes.MissingKey, "jobs.build")]
    [InlineData("type: switch\nexpression: x", DiagnosticCodes.MissingKey, "jobs.build")]
    [InlineData("type: switch\nexpression: x\ncases: []", DiagnosticCodes.WrongType, "jobs.build.cases")]
    [InlineData("type: switch\nexpression: x\ncases: [ a ]", DiagnosticCodes.WrongType, "jobs.build.cases[0]")]
    [InlineData("type: switch\nexpression: x\ncases: [ { when: a } ]", DiagnosticCodes.MissingKey, "jobs.build.cases[0]")]
    [InlineData("type: switch\nexpression: x\ncases: [ { name: a } ]", DiagnosticCodes.ConflictingKeys, "jobs.build.cases[0]")]
    [InlineData("type: switch\nexpression: x\ncases: [ { name: a, when: b, default: true } ]", DiagnosticCodes.ConflictingKeys, "jobs.build.cases[0]")]
    [InlineData("type: switch\nexpression: x\ncases: [ { name: a, default: false } ]", DiagnosticCodes.InvalidValue, "jobs.build.cases[0].default")]
    [InlineData("type: switch\nexpression: x\ncases: [ { name: a, when: [b] } ]", DiagnosticCodes.WrongType, "jobs.build.cases[0].when")]
    [InlineData("type: switch\nexpression: x\ncases: [ { name: a, default: true } ]\nsteps: [ { run: a } ]", DiagnosticCodes.UnknownKey, "jobs.build.steps")]
    [InlineData("type: join", DiagnosticCodes.MissingKey, "jobs.build")]
    [InlineData("type: join\nneeds: [a]\nimage: alpine", DiagnosticCodes.UnknownKey, "jobs.build.image")]
    public void Invalid_flow_control_jobs_are_reported(string job, string code, string path) =>
        SingleError(WithJob(job), code, path);

    [Fact]
    public void Job_ids_must_be_identifiers() =>
        SingleError("version: 1\njobs:\n  Build Job:\n    steps: [ { run: a } ]\n", DiagnosticCodes.InvalidValue, "jobs.Build Job");

    [Fact]
    public void A_job_must_be_a_mapping() =>
        SingleError("version: 1\njobs:\n  build: make\n", DiagnosticCodes.WrongType, "jobs.build");

    [Fact]
    public void Jobs_must_be_a_mapping() =>
        SingleError("version: 1\njobs: [build]\n", DiagnosticCodes.WrongType, "jobs");

    [Fact]
    public void Misspelled_job_keys_get_a_suggestion() =>
        SingleError(WithJob("need: [a]\nsteps: [ { run: a } ]"), DiagnosticCodes.UnknownKey, "jobs.build.need")
            .Message.ShouldContain("Did you mean 'needs'?");
}
