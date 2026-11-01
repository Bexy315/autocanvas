using AutoCanvas.Core.Secrets;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Connection;
using AutoCanvas.Runner.Docker;
using AutoCanvas.Runner.Execution;

namespace AutoCanvas.Runner.Tests;

/// <summary>The runner's small parts on their own.</summary>
public sealed class UnitTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void Waits_double_up_to_a_minute_and_start_over_after_a_welcome()
    {
        var backoff = new Backoff(() => 0);

        var waits = Enumerable.Range(0, 9).Select(_ => backoff.Next().TotalSeconds).ToList();
        backoff.Reset();

        waits.ShouldBe([1, 2, 4, 8, 16, 32, 60, 60, 60]);
        backoff.Next().ShouldBe(Backoff.First);
        new Backoff(() => 1).Next().ShouldBe(TimeSpan.FromSeconds(0.8));
    }

    [Theory]
    [InlineData("a=1\nb=two=2\n\n=ignored\nc = 3 \r\na=again", "a:again|b:two=2|c: 3 ")]
    [InlineData("", "")]
    [InlineData("no equals sign", "")]
    public void Output_files_are_name_value_lines(string text, string expected) =>
        string.Join('|', StepOutputs.Parse(text).OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => $"{o.Key}:{o.Value}")).ShouldBe(expected);

    [Fact]
    public async Task A_missing_output_file_means_no_outputs() =>
        (await StepOutputs.ReadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), Cancel)).ShouldBeEmpty();

    [Theory]
    [InlineData(null, "/workspace")]
    [InlineData("", "/workspace")]
    [InlineData(".", "/workspace")]
    [InlineData("src/app", "/workspace/src/app")]
    [InlineData("src\\app\\..\\lib", "/workspace/src/lib")]
    [InlineData("..", null)]
    [InlineData("/abs", null)]
    [InlineData("C:\\x", null)]
    public void Working_directories_stay_in_the_workspace(string? relative, string? expected) =>
        JobPaths.InContainer(relative).ShouldBe(expected);

    [Fact]
    public void Bind_mounts_use_the_host_path_when_the_runner_runs_in_a_container()
    {
        var paths = new JobPaths(new RunnerOptions { DataDirectory = "/data", HostDataDirectory = "C:\\runner\\data\\" }, "job-1");

        paths.Binds.ShouldBe(["C:/runner/data/work/job-1/workspace:/workspace", "C:/runner/data/work/job-1/meta:/autocanvas"]);
    }

    [Theory]
    [InlineData("alpine", "alpine", "latest")]
    [InlineData("alpine:3.22", "alpine", "3.22")]
    [InlineData("registry:5000/team/app", "registry:5000/team/app", "latest")]
    [InlineData("registry:5000/team/app:1.0", "registry:5000/team/app", "1.0")]
    [InlineData("app@sha256:abc", "app", "sha256:abc")]
    public void Image_references_split_into_repository_and_tag(string image, string repository, string tag) =>
        ImageReference.Parse(image).ShouldBe(new ImageReference(repository, tag));

    [Fact]
    public void Settings_that_cannot_work_are_refused()
    {
        var options = new RunnerOptions { Server = new Uri("ftp://x"), DataDirectory = " ", MaxConcurrentJobs = 0, DefaultImage = "", ShutdownTimeout = TimeSpan.FromDays(2) };
        options.Labels.Add(" ");

        var result = new RunnerOptionsValidator().Validate(null, options);

        result.Failures!.Count().ShouldBe(6);
        new RunnerOptionsValidator().Validate(null, new RunnerOptions { Server = new Uri("https://server:5081") }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Labels_default_to_linux_and_are_announced_once()
    {
        var options = new RunnerOptions();
        RunnerCapabilities.Of(options).Labels.ShouldBe(["linux"]);
        options.Labels.Add("docker");
        options.Labels.Add("docker");

        var capabilities = RunnerCapabilities.Of(options);

        capabilities.Labels.ShouldBe(["docker"]);
        capabilities.Shells.ShouldBe(["bash", "sh"]);
        capabilities.Executor.ShouldBe(Executor.Container);
        capabilities.RunnerVersion.ShouldStartWith("autocanvas-runner/");
    }

    [Fact]
    public async Task A_credential_file_round_trips_and_a_broken_one_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "credential.json");
        var credential = new RunnerCredential(Guid.CreateVersion7(), "acr_secret");

        await credential.WriteAsync(path, Cancel);
        (await RunnerCredential.ReadAsync(path, Cancel)).ShouldBe(credential);
        credential.ToString().ShouldNotContain("acr_secret");
        await File.WriteAllTextAsync(path, "{}", Cancel);
        await Should.ThrowAsync<InvalidDataException>(RunnerCredential.ReadAsync(path, Cancel));
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public async Task Log_lines_are_masked_split_and_chunked_by_source()
    {
        var outbox = new Outbox();
        var redactor = new SecretRedactor(["s3cret-token"]);
        var log = new LogBatcher("job", outbox, TimeProvider.System);
        await using (log)
        {
            log.Write(null, LogStream.System, "preparing", redactor);
            log.Write(0, LogStream.Stdout, "token s3cret-token", redactor);
            log.Write(0, LogStream.Stderr, new string('x', LogBatcher.MaxLineLength + 5), redactor);
            for (var i = 0; i < 5; i++)
            {
                log.Write(1, LogStream.Stdout, new string('y', LogBatcher.MaxLineLength), redactor);
            }
        }

        var chunks = Drain(outbox);
        chunks.Select(c => c.Sequence).ShouldBe([0UL, 1UL, 2UL, 3UL]);
        chunks[0].JobSetup.ShouldBeTrue();
        chunks[1].StepIndex.ShouldBe(0u);
        chunks[1].Lines.Select(l => l.Text.Length).ShouldBe(["token ***".Length, LogBatcher.MaxLineLength, 5]);
        chunks[1].Lines[0].Text.ShouldBe("token ***");
        chunks[2].Lines.Count.ShouldBe(4);
        chunks[3].Lines.Count.ShouldBe(1);
    }

    [Fact]
    public void Log_chunks_beyond_the_limit_are_dropped_until_the_stream_takes_some()
    {
        var outbox = new Outbox();
        for (var i = 0; i < Outbox.MaxPendingLogs; i++)
        {
            outbox.SendLog(new LogChunk()).ShouldBeTrue();
        }

        outbox.SendLog(new LogChunk()).ShouldBeFalse();
        outbox.Reader.TryRead(out var taken).ShouldBeTrue();
        outbox.Taken(taken!);
        outbox.SendLog(new LogChunk()).ShouldBeTrue();
    }

    [Fact]
    public void Report_files_are_found_by_their_globs_relative_to_the_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(workspace, "tests", "results"));
        File.WriteAllText(Path.Combine(workspace, "tests", "results", "b.trx"), "");
        File.WriteAllText(Path.Combine(workspace, "tests", "results", "a.trx"), "");
        File.WriteAllText(Path.Combine(workspace, "other.xml"), "");
        var report = new ReportSpec { Name = "Unit tests", Format = ReportFormat.Trx };
        report.Paths.Add("**/*.trx");

        ReportUploader.Find(report, workspace).ShouldBe(["tests/results/a.trx", "tests/results/b.trx"]);
        ReportUploader.Find(report, Path.Combine(workspace, "missing")).ShouldBeEmpty();
        Directory.Delete(workspace, recursive: true);
    }

    [Fact]
    public async Task The_entry_point_runs_the_command_line() =>
        (await RunnerProgram.Main(["--help"])).ShouldBe(0);

    private static List<LogChunk> Drain(Outbox outbox)
    {
        var chunks = new List<LogChunk>();
        while (outbox.Reader.TryRead(out var message))
        {
            chunks.Add(message.LogChunk);
        }

        return chunks;
    }
}
