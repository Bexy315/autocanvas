using AutoCanvas.Core.Orchestration;
using AutoCanvas.Core.Secrets;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Postgres;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Sqlite;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Tests;

public abstract class RunLogServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Yaml = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            runs_on: linux
            outputs:
              tag: ${{ steps.tag.outputs.tag }}
            steps:
              - id: tag
                run: echo tag
              - if: ${{ vars.NEVER == 'yes' }}
                run: echo skipped
        """;

    private static readonly HashSet<string> Linux = new(StringComparer.Ordinal) { "linux" };

    private readonly IJobClaimer claimer = provider == DatabaseProvider.Postgres ? new PostgresJobClaimer() : new SqliteJobClaimer();
    private readonly LogSignal signal = new();
    private readonly Guid runner = Guid.CreateVersion7();
    private Actor owner = null!;

    [Fact]
    public async Task Chunks_are_stored_once_masked_and_read_in_order_with_gaps_shown()
    {
        var (run, attempt) = await StartAsync();
        var redactor = await LogsAsync(l => l.RedactorAsync(attempt, Cancel));

        (await AppendAsync(attempt, 0, null, redactor!, "Pulling image")).ShouldBeTrue();
        (await AppendAsync(attempt, 0, null, redactor!, "Pulling image again")).ShouldBeFalse();
        (await AppendAsync(attempt, 2, 0, redactor!, "token is s3cret-token", "done")).ShouldBeTrue();
        var waking = signal.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
        (await AppendAsync(attempt, 3, 0, redactor!, "more")).ShouldBeTrue();
        (await waking).ShouldBeTrue();

        var all = (await ReadAsync(owner, run, attempt, null, null)).ShouldSucceed();
        var later = (await ReadAsync(owner, run, attempt, 0, 1)).ShouldSucceed();
        var rest = (await ReadAsync(owner, run, attempt, later.Next, null)).ShouldSucceed();

        all.Chunks.Select(c => c.Sequence).ShouldBe([0L, 2L, 3L]);
        all.Chunks[0].StepIndex.ShouldBeNull();
        all.Chunks[0].Lines.ShouldHaveSingleItem().Text.ShouldBe("Pulling image");
        all.Chunks[1].StepIndex.ShouldBe(0);
        all.Chunks[1].Lines.Select(l => l.Text).ShouldBe(["token is ***", "done"]);
        all.Chunks[1].Lines[0].Source.ShouldBe(LogSource.Stderr);
        all.Missing.ShouldBe([1L]);
        all.Next.ShouldBe(3);
        all.IsComplete.ShouldBeFalse();
        later.Chunks.ShouldHaveSingleItem().Sequence.ShouldBe(2);
        later.Missing.ShouldBe([1L]);
        rest.Chunks.ShouldHaveSingleItem().Sequence.ShouldBe(3);
        rest.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_log_is_complete_once_its_attempt_finished_and_everything_was_read()
    {
        var (run, attempt) = await StartAsync();
        var redactor = (await LogsAsync(l => l.RedactorAsync(attempt, Cancel)))!;
        await AppendAsync(attempt, 0, 0, redactor, "one");
        await AppendAsync(attempt, 1, 0, redactor, "two");
        await DispatchAsync(d => d.FinishAsync(attempt, runner, AttemptOutcome.Succeeded, null, Cancel));

        (await ReadAsync(owner, run, attempt, null, 1)).ShouldSucceed().IsComplete.ShouldBeFalse();
        (await ReadAsync(owner, run, attempt, 0, 1)).ShouldSucceed().IsComplete.ShouldBeTrue();
        (await ReadAsync(owner, run, attempt, 1, null)).ShouldSucceed().Chunks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_runner_holding_the_attempt_writes_its_log()
    {
        var (run, attempt) = await StartAsync();
        var redactor = (await LogsAsync(l => l.RedactorAsync(attempt, Cancel)))!;

        (await LogsAsync(l => l.AppendAsync(attempt, Guid.CreateVersion7(), Chunk(0, null, "intruder"), redactor, Cancel))).ShouldBeFalse();
        (await LogsAsync(l => l.AppendAsync(attempt, runner, Chunk(-1, null, "negative"), redactor, Cancel))).ShouldBeFalse();
        (await LogsAsync(l => l.RedactorAsync(Guid.CreateVersion7(), Cancel))).ShouldBeNull();
        (await ReadAsync(owner, run, attempt, null, null)).ShouldSucceed().Chunks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Long_lines_are_cut_and_a_log_stops_at_its_limit()
    {
        var (run, attempt) = await StartAsync();
        var redactor = (await LogsAsync(l => l.RedactorAsync(attempt, Cancel)))!;
        var limit = new LogOptions { MaxBytesPerJob = 1024 * 1024 };

        await LogsAsync(l => l.AppendAsync(attempt, runner, Chunk(0, 0, new string('a', LogIngestion.MaxLineLength + 10)), redactor, Cancel), limit);
        await LogsAsync(l => l.AppendAsync(attempt, runner, Chunk(1, 0, [.. Enumerable.Range(0, 70).Select(_ => new string('b', LogIngestion.MaxLineLength))]), redactor, Cancel), limit);
        (await LogsAsync(l => l.AppendAsync(attempt, runner, Chunk(2, 0, "after the limit"), redactor, Cancel), limit)).ShouldBeFalse();

        var page = (await ReadAsync(owner, run, attempt, null, null)).ShouldSucceed();
        page.Chunks[0].Lines.ShouldHaveSingleItem().Text.ShouldEndWith(" [line cut]");
        page.Chunks[1].Lines.ShouldHaveSingleItem().Text.ShouldContain("limit of 1 MiB");
        page.Chunks[1].Lines[0].Source.ShouldBe(LogSource.System);
        page.IsTruncated.ShouldBeTrue();
        (await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == attempt, Cancel))).LogBytes.ShouldBe(LogIngestion.MaxLineLength + 11);
    }

    [Fact]
    public async Task Chunks_with_too_many_lines_keep_the_first_ones()
    {
        var (run, attempt) = await StartAsync();
        var redactor = (await LogsAsync(l => l.RedactorAsync(attempt, Cancel)))!;

        await LogsAsync(l => l.AppendAsync(attempt, runner, Chunk(0, 0, [.. Enumerable.Range(0, LogIngestion.MaxLinesPerChunk + 3).Select(i => $"line {i}")]), redactor, Cancel));

        var lines = (await ReadAsync(owner, run, attempt, null, null)).ShouldSucceed().Chunks.ShouldHaveSingleItem().Lines;
        lines.Count.ShouldBe(LogIngestion.MaxLinesPerChunk + 1);
        lines[^1].Text.ShouldBe("[3 more lines of this chunk were dropped]");
    }

    [Fact]
    public async Task Reading_needs_viewer_and_the_attempt_of_that_run()
    {
        var (run, attempt) = await StartAsync();
        var (otherRun, _) = await StartAsync();
        var stranger = await ActorAsync("stranger");

        (await ReadAsync(stranger, run, attempt, null, null)).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(owner, otherRun, attempt, null, null)).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(owner, Guid.CreateVersion7(), attempt, null, null)).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(owner, run, attempt, null, 0)).ShouldFail(UseCaseErrorKind.Validation);
        (await JobAsync(stranger, run, "build")).ShouldFail(UseCaseErrorKind.NotFound);
        (await JobAsync(owner, run, "missing")).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task A_job_shows_its_outputs_attempts_and_steps()
    {
        var (run, attempt) = await StartAsync();
        await ProgressAsync(p => p.PreparedAsync(attempt, runner, null, Cancel));
        await ProgressAsync(p => p.StepCompletedAsync(attempt, runner, new StepReport(0, StepOutcome.Succeeded, 0, new Dictionary<string, string>(StringComparer.Ordinal) { ["tag"] = "v3" }, null, Clock.GetUtcNow()), Cancel));
        await ProgressAsync(p => p.FinishedAsync(attempt, runner, ["no reports"], Cancel));

        var job = (await JobAsync(owner, run, "build")).ShouldSucceed();

        job.Status.ShouldBe(RunJobStatus.Succeeded);
        job.Outputs.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal) { ["tag"] = "v3" });
        var only = job.Attempts.ShouldHaveSingleItem();
        only.Id.ShouldBe(attempt);
        only.Status.ShouldBe(AttemptStatus.Succeeded);
        only.RunnerId.ShouldBe(runner);
        only.RunnerName.ShouldBeNull();
        only.Warnings.ShouldBe(["no reports"]);
        only.Steps.Select(s => (s.Index, s.Status)).ShouldBe([(0, JobStepStatus.Succeeded), (1, JobStepStatus.Skipped)]);
        only.Steps[0].Outputs["tag"].ShouldBe("v3");
        only.Steps[1].Reason!.ShouldContain("condition is false");
    }

    private async Task<(Guid Run, Guid Attempt)> StartAsync()
    {
        var unique = Guid.CreateVersion7().ToString("N")[^8..];
        owner = await ActorAsync("owner" + unique);
        var folder = (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder("logs" + unique), Cancel))).ShouldSucceed().Id;
        (await SecretsAsync(s => s.SetAsync(owner, ConfigurationScope.Folder(folder), "TOKEN", "s3cret-token", Cancel))).ShouldSucceed();
        var automation = (await AutomationsAsync(a => a.CreateAsync(owner, folder, "flow", Yaml, null, Cancel))).ShouldSucceed().Id;
        var run = (await RunsAsync(r => r.StartAsync(owner, automation, Cancel))).ShouldSucceed().Id;
        var attempt = (await DispatchAsync(d => d.ClaimAsync(runner, Linux, Linux, runsContainers: true, Cancel))).ShouldNotBeNull();
        (await DispatchAsync(d => d.AcceptAsync(attempt, runner, Cancel))).ShouldBeTrue();
        return (run, attempt);
    }

    private static ReceivedLogChunk Chunk(long sequence, int? step, params string[] lines) =>
        new(sequence, step, [.. lines.Select((l, i) => new LogLine(DateTimeOffset.UnixEpoch.AddSeconds(i), i == 0 && step is not null ? LogSource.Stderr : LogSource.Stdout, l))]);

    private Task<bool> AppendAsync(Guid attempt, long sequence, int? step, SecretRedactor redactor, params string[] lines) =>
        LogsAsync(l => l.AppendAsync(attempt, runner, Chunk(sequence, step, lines), redactor, Cancel));

    private async Task<UseCaseResult<LogPage>> ReadAsync(Actor actor, Guid run, Guid attempt, long? after, int? limit)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await new RunLogService(context).ReadAsync(actor, run, attempt, after, limit, Cancel);
        }
    }

    private async Task<UseCaseResult<RunJobDetailView>> JobAsync(Actor actor, Guid run, string key)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await new RunLogService(context).GetJobAsync(actor, run, key, Cancel);
        }
    }

    private async Task<T> LogsAsync<T>(Func<LogIngestion, Task<T>> action, LogOptions? options = null)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new LogIngestion(context, new ConfigurationService(context, new SecretCipher(Key)), signal, Options.Create(options ?? new LogOptions()), Clock));
        }
    }

    private async Task<T> DispatchAsync<T>(Func<JobDispatcher, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(Dispatcher(context));
        }
    }

    private async Task ProgressAsync<T>(Func<JobProgress, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            await action(new JobProgress(context, new ConfigurationService(context, new SecretCipher(Key)), Dispatcher(context), Clock));
        }
    }

    private JobDispatcher Dispatcher(AutoCanvasDbContext context) =>
        new(context, claimer, Signal, new ConfigurationService(context, new SecretCipher(Key)), Options.Create(new QueueOptions()), Clock);
}

public sealed class RunLogServiceTestsOnSqlite() : RunLogServiceTests(DatabaseProvider.Sqlite);

public sealed class RunLogServiceTestsOnPostgres() : RunLogServiceTests(DatabaseProvider.Postgres);
