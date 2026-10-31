using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class JobOfferBuilderTests(DatabaseProvider provider) : IdentityTest(provider)
{
    [Fact]
    public async Task The_offer_is_resolved_and_carries_no_secrets()
    {
        var attempt = await StartAsync("""
            version: 1
            triggers:
              manual: true
            variables:
              CONFIGURATION: Release
            jobs:
              test:
                name: Unit tests
                runs_on: linux
                image: mcr.microsoft.com/dotnet/sdk:10.0
                timeout_minutes: 15
                checkout:
                  - repository: https://example.org/team/app.git
                    ref: main
                    path: app
                  - repository: https://example.org/team/lib.git
                services:
                  db:
                    image: postgres:17
                    env:
                      POSTGRES_PASSWORD: ${{ secrets.DB_PASSWORD }}
                    ready: pg_isready
                env:
                  CONFIGURATION: ${{ vars.CONFIGURATION }}
                  RUN: ${{ run.automation_name }}
                  TOKEN: Bearer ${{ secrets.DB_PASSWORD }}
                steps:
                  - run: dotnet test
                reports:
                  - format: trx
                    path: '**/*.trx'
                  - format: cobertura
                    path: [a.xml, b.xml]
                    name: Coverage
            """, secret: "s3cret-value");

        var offered = (await BuildAsync(attempt)).ShouldSucceed();

        offered.AttemptId.ShouldBe(attempt);
        offered.Attempt.ShouldBe(1);
        offered.AutomationName.ShouldBe("flow");
        offered.JobKey.ShouldBe("test");
        offered.DisplayName.ShouldBe("Unit tests");
        offered.Timeout.ShouldBe(TimeSpan.FromMinutes(15));
        offered.Image.ShouldBe("mcr.microsoft.com/dotnet/sdk:10.0");
        offered.Env.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal) { ["CONFIGURATION"] = "Release", ["RUN"] = "flow" }, ignoreOrder: true);
        offered.Checkouts.ShouldBe([
            new OfferedCheckout(new Uri("https://example.org/team/app.git"), "main", "app", 1),
            new OfferedCheckout(new Uri("https://example.org/team/lib.git"), null, null, 1),
        ]);
        var service = offered.Services.ShouldHaveSingleItem();
        service.Name.ShouldBe("db");
        service.Image.ShouldBe("postgres:17");
        service.Env["POSTGRES_PASSWORD"].ShouldBe("s3cret-value");
        service.ReadyCommand.ShouldBe("pg_isready");
        service.ReadyTimeout.ShouldBe(JobOfferBuilder.ServiceReadyTimeout);
        offered.Reports.Select(r => (r.Format, r.Name, string.Join(',', r.Paths))).ShouldBe([("trx", "trx", "**/*.trx"), ("cobertura", "Coverage", "a.xml,b.xml")]);
    }

    [Fact]
    public async Task Jobs_without_an_image_or_checkout_get_the_runner_defaults()
    {
        var attempt = await StartAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              plain:
                checkout: false
                steps:
                  - run: echo hi
            """);

        var offered = (await BuildAsync(attempt)).ShouldSucceed();

        offered.Image.ShouldBeNull();
        offered.Checkouts.ShouldBeEmpty();
        offered.Services.ShouldBeEmpty();
        offered.DisplayName.ShouldBe("plain");
        offered.Timeout.ShouldBe(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public async Task Checking_out_connected_repositories_is_refused_with_a_reason()
    {
        var attempt = await StartAsync("version: 1\ntriggers:\n  manual: true\njobs:\n  a:\n    checkout:\n      - repository: self\n    steps:\n      - run: echo hi\n");

        (await BuildAsync(attempt)).ShouldFail(UseCaseErrorKind.Validation).Message.ShouldContain("connected repository");
    }

    [Fact]
    public async Task Service_presets_bring_their_image_environment_and_readiness_check()
    {
        var attempt = await StartAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              test:
                services:
                  db:
                    preset: postgres
                    version: 16
                    env:
                      POSTGRES_PASSWORD: test
                  cache:
                    preset: redis
                  sql:
                    preset: mysql
                    ready: mysqladmin ping
                steps:
                  - run: echo test
            """);

        var services = (await BuildAsync(attempt)).ShouldSucceed().Services.ToDictionary(s => s.Name, StringComparer.Ordinal);

        services["db"].Image.ShouldBe("postgres:16");
        services["db"].Env["POSTGRES_PASSWORD"].ShouldBe("test");
        services["db"].ReadyCommand.ShouldBe("pg_isready -U postgres");
        services["cache"].Image.ShouldBe("redis:7");
        services["cache"].ReadyCommand.ShouldBe("redis-cli ping");
        services["sql"].Image.ShouldBe("mysql:8.4");
        services["sql"].Env["MYSQL_ROOT_PASSWORD"].ShouldBe("mysql");
        services["sql"].ReadyCommand.ShouldBe("mysqladmin ping");
    }

    [Fact]
    public async Task Unknown_attempts_are_not_found()
    {
        (await BuildAsync(Guid.CreateVersion7())).ShouldFail(UseCaseErrorKind.NotFound);
    }

    private async Task<Guid> StartAsync(string yaml, string? secret = null)
    {
        var ada = await ActorAsync("ada", isAdmin: true);
        var folder = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("team"), Cancel))).ShouldSucceed().Id;
        if (secret is not null)
        {
            (await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Folder(folder), "DB_PASSWORD", secret, Cancel))).ShouldSucceed();
        }

        var automation = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "flow", yaml, null, Cancel))).ShouldSucceed().Id;
        var run = (await RunsAsync(r => r.StartAsync(ada, automation, Cancel))).ShouldSucceed().Id;
        return await ReadAsync(db => db.JobAttempts.Join(db.RunJobs.Where(j => j.RunId == run), a => a.RunJobId, j => j.Id, (a, _) => a.Id).SingleAsync(Cancel));
    }

    private async Task<UseCaseResult<OfferedJob>> BuildAsync(Guid attempt)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await new JobOfferBuilder(context, new ConfigurationService(context, new SecretCipher(Key)), Clock).BuildAsync(attempt, Cancel);
        }
    }
}

public sealed class JobOfferBuilderTestsOnSqlite() : JobOfferBuilderTests(DatabaseProvider.Sqlite);

public sealed class JobOfferBuilderTestsOnPostgres() : JobOfferBuilderTests(DatabaseProvider.Postgres);
