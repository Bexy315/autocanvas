using System.Text;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Reports;
using AutoCanvas.Persistence.Sqlite;
using AutoCanvas.Reports;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Reports;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Tests;

public abstract class ReportIngestionTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Yaml = """
        version: 1
        triggers:
          manual: true
        jobs:
          test:
            runs_on: linux
            retry:
              max_attempts: 2
            steps:
              - run: make test
            reports:
              - format: junit
                path: reports/*.xml
                name: Unit tests
              - format: cobertura
                path: coverage.xml
        """;

    private const string JUnit = """
        <testsuite name="Cart">
          <testcase classname="shop.CartTest" name="sums" time="0.25"/>
          <testcase classname="shop.CartTest" name="pays" time="1.5"><failure message="token s3cret-token was refused">stack</failure></testcase>
          <testcase classname="shop.CartTest" name="later"><skipped/></testcase>
        </testsuite>
        """;

    private const string Cobertura = """
        <coverage>
          <packages>
            <package name="shop">
              <classes>
                <class name="Cart" filename="shop/cart.py">
                  <lines>
                    <line number="1" hits="1"/>
                    <line number="2" hits="0"/>
                    <line number="3" hits="0"/>
                    <line number="5" hits="1" branch="true" condition-coverage="50% (1/2)"/>
                  </lines>
                </class>
              </classes>
            </package>
          </packages>
        </coverage>
        """;

    private static readonly HashSet<string> Linux = new(StringComparer.Ordinal) { "linux" };

    private readonly IJobClaimer claimer = provider == DatabaseProvider.Postgres ? new PostgresJobClaimer() : new SqliteJobClaimer();
    private readonly Guid runner = Guid.CreateVersion7();
    private Actor owner = null!;
    private Guid automation;

    [Fact]
    public async Task Test_reports_are_stored_with_their_counts_and_masked_texts()
    {
        var (run, attempt) = await StartAsync();

        var id = await UploadAsync(attempt, ReportFormat.JUnit, "Unit tests", "reports/junit.xml", JUnit);

        var cases = await ReadAsync(db => db.ReportTestCases.Where(c => c.ReportId == id).OrderBy(c => c.Ordinal).ToListAsync(Cancel));
        cases.Select(c => (c.FullName, c.Outcome)).ShouldBe([
            ("shop.CartTest.sums", TestCaseOutcome.Passed),
            ("shop.CartTest.pays", TestCaseOutcome.Failed),
            ("shop.CartTest.later", TestCaseOutcome.Skipped),
        ]);
        cases[1].Message.ShouldBe("token *** was refused");
        cases[1].DurationMs.ShouldBe(1500);
        cases[0].NameHash.ShouldBe(ReportRows.Hash("shop.CartTest.sums"));
        var report = (await ListAsync(owner, run)).ShouldSucceed().Reports.ShouldHaveSingleItem();
        report.Id.ShouldBe(id);
        report.JobKey.ShouldBe("test");
        report.Kind.ShouldBe(ReportKind.Tests);
        report.Format.ShouldBe("junit");
        report.Tests.ShouldBe(new TestTotalsView(3, 1, 1, 1, 1750));
        report.Coverage.ShouldBeNull();
        report.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Coverage_reports_keep_their_packages_and_classes()
    {
        var (run, attempt) = await StartAsync();

        var id = await UploadAsync(attempt, ReportFormat.Cobertura, "cobertura", "coverage.xml", Cobertura);

        var package = await ReadAsync(db => db.ReportCoveragePackages.SingleAsync(p => p.ReportId == id, Cancel));
        var type = await ReadAsync(db => db.ReportCoverageClasses.SingleAsync(c => c.ReportId == id, Cancel));
        package.Name.ShouldBe("shop");
        type.Name.ShouldBe("Cart");
        type.File.ShouldBe("shop/cart.py");
        type.Uncovered.ShouldBe("2-3");
        var reports = (await ListAsync(owner, run)).ShouldSucceed();
        reports.Coverage.ShouldBe(new CoverageTotalsView(2, 4, 1, 2));
        reports.Coverage!.LineRate.ShouldBe(0.5);
        reports.Tests.ShouldBeNull();
        reports.Reports.ShouldHaveSingleItem().Kind.ShouldBe(ReportKind.Coverage);
    }

    [Fact]
    public async Task A_file_that_is_no_report_is_kept_with_the_reason()
    {
        var (run, attempt) = await StartAsync();

        await UploadAsync(attempt, ReportFormat.JUnit, "Unit tests", "reports/broken.xml", "this is not xml");

        var report = (await ListAsync(owner, run)).ShouldSucceed().Reports.ShouldHaveSingleItem();
        report.Error.ShouldNotBeNull().ShouldContain("not valid XML");
        report.Tests.ShouldBeNull();
    }

    [Fact]
    public async Task A_file_uploaded_again_returns_the_stored_report()
    {
        var (_, attempt) = await StartAsync();
        var id = await UploadAsync(attempt, ReportFormat.JUnit, "Unit tests", "reports/junit.xml", JUnit);

        var again = (await BeginAsync(File(attempt, ReportFormat.JUnit, "Unit tests", "reports/junit.xml"))).ShouldSucceed();

        again.StoredId.ShouldBe(id);
        (await IngestAsync(i => i.StoreAsync(again, Stream.Null, Cancel))).ShouldBe(id);
        (await ReadAsync(db => db.Reports.CountAsync(Cancel))).ShouldBe(1);
    }

    [Fact]
    public async Task Only_the_runner_of_a_running_job_uploads_reports_the_job_declares()
    {
        var (_, attempt) = await StartAsync();

        (await BeginAsync(File(attempt, ReportFormat.JUnit, "Unit tests", "a.xml"), Guid.CreateVersion7())).ShouldFail(UseCaseErrorKind.NotFound);
        (await BeginAsync(File(Guid.CreateVersion7(), ReportFormat.JUnit, "Unit tests", "a.xml"))).ShouldFail(UseCaseErrorKind.NotFound);
        (await BeginAsync(File(attempt, ReportFormat.Trx, "Unit tests", "a.trx"))).ShouldFail(UseCaseErrorKind.Validation).Message.ShouldContain("no trx report named 'Unit tests'");
        (await BeginAsync(File(attempt, ReportFormat.JUnit, "junit", "a.xml"))).ShouldFail(UseCaseErrorKind.Validation);
        (await BeginAsync(File(attempt, ReportFormat.JUnit, "Unit tests", " "))).ShouldFail(UseCaseErrorKind.Validation);

        await DispatchAsync(d => d.FinishAsync(attempt, runner, AttemptOutcome.Succeeded, null, Cancel));
        (await BeginAsync(File(attempt, ReportFormat.JUnit, "Unit tests", "a.xml"))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
    }

    [Fact]
    public async Task Files_beyond_the_limits_are_refused()
    {
        var (_, attempt) = await StartAsync();
        var limits = new ReportOptions { MaxBytesPerFile = 100, MaxFilesPerJob = 1 };

        (await BeginAsync(File(attempt, ReportFormat.JUnit, "Unit tests", "big.xml") with { Size = 101 }, options: limits)).ShouldFail(UseCaseErrorKind.TooManyRequests);
        await UploadAsync(attempt, ReportFormat.JUnit, "Unit tests", "reports/junit.xml", JUnit);
        (await BeginAsync(File(attempt, ReportFormat.JUnit, "Unit tests", "reports/second.xml"), options: limits)).ShouldFail(UseCaseErrorKind.TooManyRequests).Message.ShouldContain("1 reports already");
    }

    [Fact]
    public async Task Run_totals_count_each_job_once_and_the_job_shows_its_reports()
    {
        var (run, first) = await StartAsync();
        await UploadAsync(first, ReportFormat.JUnit, "Unit tests", "reports/junit.xml", JUnit);
        await DispatchAsync(d => d.FinishAsync(first, runner, AttemptOutcome.Failed, "tests failed", Cancel));
        Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await DispatchAsync(d => d.ClaimAsync(runner, Linux, Linux, runsContainers: true, Cancel))).ShouldNotBeNull();
        (await DispatchAsync(d => d.AcceptAsync(second, runner, Cancel))).ShouldBeTrue();
        await UploadAsync(second, ReportFormat.JUnit, "Unit tests", "reports/a.xml", """<testsuite><testcase classname="shop.CartTest" name="pays"/></testsuite>""");
        await UploadAsync(second, ReportFormat.JUnit, "Unit tests", "reports/b.xml", """<testsuite><testcase classname="shop.CartTest" name="sums"/></testsuite>""");

        var reports = (await ListAsync(owner, run)).ShouldSucceed();
        var job = (await JobAsync(owner, run)).ShouldSucceed();

        reports.Reports.Select(r => r.AttemptId).ShouldBe([first, second, second]);
        reports.Tests.ShouldBe(new TestTotalsView(2, 2, 0, 0, 0));
        job.Attempts.Select(a => a.Reports.Count).ShouldBe([1, 2]);
        job.Attempts[0].Reports[0].Tests!.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task Reading_reports_needs_viewer()
    {
        var (run, _) = await StartAsync();
        var stranger = await ActorAsync("stranger");

        (await ListAsync(stranger, run)).ShouldFail(UseCaseErrorKind.NotFound);
        (await ListAsync(owner, Guid.CreateVersion7())).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task The_tests_of_a_report_come_by_page_and_by_outcome()
    {
        var (run, attempt) = await StartAsync();
        var id = await UploadAsync(attempt, ReportFormat.JUnit, "Unit tests", "reports/junit.xml", JUnit);
        var (otherRun, _) = await NextRunAsync();

        var first = (await ReadReportsAsync(r => r.TestsAsync(owner, run, id, null, new PageRequest(2), Cancel))).ShouldSucceed();
        var rest = (await ReadReportsAsync(r => r.TestsAsync(owner, run, id, null, new PageRequest(2, first.Next), Cancel))).ShouldSucceed();
        var failed = (await ReadReportsAsync(r => r.TestsAsync(owner, run, id, "FAILED", new PageRequest(), Cancel))).ShouldSucceed();
        var detail = (await ReadReportsAsync(r => r.GetAsync(owner, run, id, Cancel))).ShouldSucceed();
        var stranger = await ActorAsync("stranger");

        first.Items.Select(c => c.Name).ShouldBe(["sums", "pays"]);
        first.Next.ShouldBe("1");
        rest.Items.ShouldHaveSingleItem().Name.ShouldBe("later");
        rest.Next.ShouldBeNull();
        var test = failed.Items.ShouldHaveSingleItem();
        test.FullName.ShouldBe("shop.CartTest.pays");
        test.Message.ShouldBe("token *** was refused");
        test.StackTrace.ShouldBe("stack");
        test.Group.ShouldBe("Cart");
        detail.Report.Id.ShouldBe(id);
        detail.Packages.ShouldBeEmpty();
        (await ReadReportsAsync(r => r.TestsAsync(owner, run, id, "broken", new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await ReadReportsAsync(r => r.TestsAsync(owner, run, id, "7", new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await ReadReportsAsync(r => r.TestsAsync(owner, run, id, null, new PageRequest(Cursor: "x"), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await ReadReportsAsync(r => r.TestsAsync(owner, run, id, null, new PageRequest(0), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await ReadReportsAsync(r => r.TestsAsync(owner, otherRun, id, null, new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadReportsAsync(r => r.GetAsync(stranger, run, id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Coverage_comes_by_package_and_class()
    {
        var (run, attempt) = await StartAsync();
        var id = await UploadAsync(attempt, ReportFormat.Cobertura, "cobertura", "coverage.xml", """
            <coverage><packages>
              <package name="shop"><classes><class name="Cart" filename="cart.py"><lines><line number="1" hits="1"/></lines></class></classes></package>
              <package name="web"><classes>
                <class name="Page" filename="page.py"><lines><line number="1" hits="0"/><line number="2" hits="0"/></lines></class>
                <class name="Form" filename="form.py"><lines><line number="4" hits="1"/></lines></class>
              </classes></package>
            </packages></coverage>
            """);

        var detail = (await ReadReportsAsync(r => r.GetAsync(owner, run, id, Cancel))).ShouldSucceed();
        var web = (await ReadReportsAsync(r => r.ClassesAsync(owner, run, id, 1, new PageRequest(1), Cancel))).ShouldSucceed();
        var all = (await ReadReportsAsync(r => r.ClassesAsync(owner, run, id, null, new PageRequest(), Cancel))).ShouldSucceed();

        detail.Packages.Select(p => (p.Index, p.Name, p.Coverage.LinesValid)).ShouldBe([(0, "shop", 1), (1, "web", 3)]);
        web.Items.ShouldHaveSingleItem().Name.ShouldBe("Form");
        var rest = (await ReadReportsAsync(r => r.ClassesAsync(owner, run, id, 1, new PageRequest(1, web.Next), Cancel))).ShouldSucceed();
        var page = rest.Items.ShouldHaveSingleItem();
        page.Name.ShouldBe("Page");
        page.Package.ShouldBe(1);
        page.Uncovered.ShouldBe("1-2");
        page.Coverage.LineRate.ShouldBe(0);
        rest.Next.ShouldBeNull();
        all.Items.Select(c => c.Name).ShouldBe(["Cart", "Form", "Page"]);
        (await ReadReportsAsync(r => r.ClassesAsync(owner, run, id, null, new PageRequest(Cursor: "-1"), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await ReadReportsAsync(r => r.ClassesAsync(owner, run, Guid.CreateVersion7(), null, new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task The_history_of_a_test_follows_it_across_runs()
    {
        var (_, first) = await StartAsync();
        await UploadAsync(first, ReportFormat.JUnit, "Unit tests", "reports/junit.xml", JUnit);
        await DispatchAsync(d => d.FinishAsync(first, runner, AttemptOutcome.Succeeded, null, Cancel));
        var (secondRun, second) = await NextRunAsync();
        await UploadAsync(second, ReportFormat.JUnit, "Unit tests", "reports/junit.xml", """<testsuite><testcase classname="shop.CartTest" name="pays" time="2"/></testsuite>""");
        var stranger = await ActorAsync("stranger");

        var history = (await HistoryAsync(owner, automation, "shop.CartTest.pays", null)).ShouldSucceed();
        var newest = (await HistoryAsync(owner, automation, "shop.CartTest.pays", 1)).ShouldSucceed();

        history.Entries.Select(e => (e.RunNumber, e.Outcome)).ShouldBe([(2, TestCaseOutcome.Passed), (1, TestCaseOutcome.Failed)]);
        (history.Passed, history.Failed, history.Skipped).ShouldBe((1, 1, 0));
        history.Entries[0].RunId.ShouldBe(secondRun);
        history.Entries[0].AttemptId.ShouldBe(second);
        history.Entries[0].Attempt.ShouldBe(1);
        history.Entries[0].JobKey.ShouldBe("test");
        history.Entries[0].DurationMs.ShouldBe(2000);
        history.Entries[1].Message.ShouldBe("token *** was refused");
        newest.Entries.ShouldHaveSingleItem().RunNumber.ShouldBe(2);
        (await HistoryAsync(owner, automation, "shop.CartTest.unknown", null)).ShouldSucceed().Entries.ShouldBeEmpty();
        (await HistoryAsync(owner, automation, "", null)).ShouldFail(UseCaseErrorKind.Validation);
        (await HistoryAsync(owner, automation, "x", 0)).ShouldFail(UseCaseErrorKind.Validation);
        (await HistoryAsync(stranger, automation, "shop.CartTest.pays", null)).ShouldFail(UseCaseErrorKind.NotFound);
        (await HistoryAsync(owner, Guid.CreateVersion7(), "shop.CartTest.pays", null)).ShouldFail(UseCaseErrorKind.NotFound);
    }

    private async Task<(Guid Run, Guid Attempt)> StartAsync()
    {
        var unique = Guid.CreateVersion7().ToString("N")[^8..];
        owner = await ActorAsync("owner" + unique);
        var folder = (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder("reports" + unique), Cancel))).ShouldSucceed().Id;
        (await SecretsAsync(s => s.SetAsync(owner, ConfigurationScope.Folder(folder), "TOKEN", "s3cret-token", Cancel))).ShouldSucceed();
        automation = (await AutomationsAsync(a => a.CreateAsync(owner, folder, "flow", Yaml, null, Cancel))).ShouldSucceed().Id;
        return await NextRunAsync();
    }

    private async Task<(Guid Run, Guid Attempt)> NextRunAsync()
    {
        var run = (await RunsAsync(r => r.StartAsync(owner, automation, Cancel))).ShouldSucceed().Id;
        var attempt = (await DispatchAsync(d => d.ClaimAsync(runner, Linux, Linux, runsContainers: true, Cancel))).ShouldNotBeNull();
        (await DispatchAsync(d => d.AcceptAsync(attempt, runner, Cancel))).ShouldBeTrue();
        return (run, attempt);
    }

    private static ReportFile File(Guid attempt, ReportFormat format, string name, string path) => new(attempt, format, name, path, 10);

    private async Task<Guid> UploadAsync(Guid attempt, ReportFormat format, string name, string path, string xml)
    {
        var receipt = (await BeginAsync(File(attempt, format, name, path))).ShouldSucceed();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return await IngestAsync(i => i.StoreAsync(receipt, content, Cancel));
    }

    private Task<UseCaseResult<ReportReceipt>> BeginAsync(ReportFile file, Guid? asRunner = null, ReportOptions? options = null) =>
        IngestAsync(i => i.BeginAsync(asRunner ?? runner, file, Cancel), options);

    private async Task<T> IngestAsync<T>(Func<ReportIngestion, Task<T>> action, ReportOptions? options = null)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new ReportIngestion(context, new ConfigurationService(context, new SecretCipher(Key)), Options.Create(options ?? new ReportOptions()), Clock));
        }
    }

    private async Task<UseCaseResult<RunReportsView>> ListAsync(Actor actor, Guid run)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await new RunReportService(context).ListAsync(actor, run, Cancel);
        }
    }

    private async Task<T> ReadReportsAsync<T>(Func<RunReportService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new RunReportService(context));
        }
    }

    private async Task<UseCaseResult<TestHistoryView>> HistoryAsync(Actor actor, Guid automationId, string name, int? limit)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await new TestHistoryService(context).HistoryAsync(actor, automationId, name, limit, Cancel);
        }
    }

    private async Task<UseCaseResult<RunJobDetailView>> JobAsync(Actor actor, Guid run)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await new RunLogService(context).GetJobAsync(actor, run, "test", Cancel);
        }
    }

    private async Task<T> DispatchAsync<T>(Func<JobDispatcher, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new JobDispatcher(context, claimer, Signal, new ConfigurationService(context, new SecretCipher(Key)), Options.Create(new QueueOptions()), Clock));
        }
    }
}

public sealed class ReportIngestionTestsOnSqlite() : ReportIngestionTests(DatabaseProvider.Sqlite);

public sealed class ReportIngestionTestsOnPostgres() : ReportIngestionTests(DatabaseProvider.Postgres);
