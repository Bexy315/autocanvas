using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Tests;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Runner.Tests;

/// <summary>
/// What "S2 done when" asks for, end to end: the real server on each database, the reference
/// runner, and Docker. Skipped without Docker as in <see cref="DockerTests"/>.
/// </summary>
public abstract class S2WalkThroughTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private const string Pipeline = """
        version: 1
        triggers:
          manual: true
        jobs:
          test:
            runs_on: linux
            image: alpine:3.22
            services:
              web:
                image: nginx:1.27-alpine
                ready: wget -q -O /dev/null http://localhost
            outputs:
              greeting: ${{ steps.check.outputs.greeting }}
            steps:
              - id: check
                shell: sh
                env:
                  TOKEN: ${{ secrets.TOKEN }}
                run: |
                  wget -q -O - http://web | grep -o 'Welcome to nginx'
                  echo "the token is $TOKEN"
                  echo "greeting=hello" >> "$AUTOCANVAS_OUTPUT"
                  mkdir -p out
                  printf '%s' '<TestRun><Results><UnitTestResult testName="Shop.Cart.sums" outcome="Passed" duration="00:00:01"/><UnitTestResult testName="Shop.Cart.pays" outcome="Failed"/></Results></TestRun>' > out/tests.trx
                  printf '%s' '<coverage><packages><package name="shop"><classes><class name="Cart" filename="cart.cs"><lines><line number="1" hits="1"/><line number="2" hits="0"/></lines></class></classes></package></packages></coverage>' > out/coverage.xml
            reports:
              - format: trx
                path: out/*.trx
              - format: cobertura
                path: out/coverage.xml
          publish:
            runs_on: linux
            image: alpine:3.22
            needs: [test]
            steps:
              - shell: sh
                run: echo "publishing ${{ jobs.test.outputs.greeting }}"
        """;

    private const string Quick = """
        version: 1
        triggers:
          schedule:
            - interval: 10m
          webhook: true
        jobs:
          build:
            runs_on: linux
            image: alpine:3.22
            steps:
              - shell: sh
                run: echo "started by ${{ trigger.event }}"
        """;

    private const string Slow = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            runs_on: linux
            image: alpine:3.22
            retry:
              max_attempts: 2
            steps:
              - shell: sh
                run: |
                  echo started
                  sleep 8
                  echo done
        """;

    [Fact]
    public async Task A_run_with_a_service_and_two_jobs_streams_its_log_masks_its_secret_and_reports()
    {
        using var docker = await DockerTests.DockerAsync();
        await using var runner = await TestRunner.CreateAsync(docker, Database);
        await runner.RegisterAsync();
        await runner.StartAsync();
        var folder = (await (await runner.Admin.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        (await runner.Admin.PutAsJsonAsync($"/api/v1/folders/{folder}/secrets/TOKEN", new { value = "s3cret-token" }, Cancel)).EnsureSuccessStatusCode();

        var run = await runner.StartRunAsync(Pipeline, folder);
        var live = await FollowAsync(runner, run, "test");

        var ended = await runner.EndedRunAsync(run, TimeSpan.FromMinutes(3));
        ended.GetProperty("state").GetString().ShouldBe("Succeeded");
        ended.GetProperty("jobs").EnumerateArray().Select(j => j.GetProperty("status").GetString()).ShouldBe(["Succeeded", "Succeeded"]);
        live.ShouldContain(e => e == "chunk");
        live[^1].ShouldBe("end");
        var log = await runner.LogAsync(run, "test");
        log.ShouldContain(l => l == "the token is ***");
        log.ShouldNotContain(l => l.Contains("s3cret-token", StringComparison.Ordinal));
        (await runner.LogAsync(run, "publish")).ShouldContain(l => l == "publishing hello");
        var reports = await runner.GetAsync($"/api/v1/runs/{run}/reports");
        reports.GetProperty("tests").GetProperty("total").GetInt32().ShouldBe(2);
        reports.GetProperty("tests").GetProperty("failed").GetInt32().ShouldBe(1);
        reports.GetProperty("coverage").GetProperty("lineRate").GetDouble().ShouldBe(0.5);
        var trx = reports.GetProperty("reports").EnumerateArray().Single(r => string.Equals(r.GetProperty("format").GetString(), "trx", StringComparison.Ordinal)).GetProperty("id").GetGuid();
        var failed = await runner.GetAsync($"/api/v1/runs/{run}/reports/{trx}/tests?outcome=failed");
        failed.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("fullName").GetString().ShouldBe("Shop.Cart.pays");
    }

    [Fact]
    public async Task A_schedule_and_a_signed_webhook_start_runs_the_runner_executes()
    {
        using var docker = await DockerTests.DockerAsync();
        await using var runner = await TestRunner.CreateAsync(docker, Database);
        await runner.RegisterAsync();
        var folder = (await (await runner.Admin.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        var automation = (await (await runner.Admin.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "quick", yaml = Quick }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        await runner.StartAsync();

        runner.Server.Clock.Advance(TimeSpan.FromMinutes(11));
        var bySchedule = await RunOfAsync(runner, automation, "Schedule");
        var hook = await (await runner.Admin.PostAsJsonAsync($"/api/v1/automations/{automation}/webhook", new { requireSignature = true }, Cancel)).JsonAsync();
        using var outsider = runner.Server.CreateClient();
        const string Body = """{ "ref": "main" }""";
        using var call = new HttpRequestMessage(HttpMethod.Post, new Uri(hook.GetProperty("path").GetString()!, UriKind.Relative)) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        call.Headers.Add("X-AutoCanvas-Signature", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(hook.GetProperty("signingSecret").GetString()!), Encoding.UTF8.GetBytes(Body))));
        var accepted = await outsider.SendAsync(call, Cancel);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var byWebhook = (await accepted.JsonAsync()).GetProperty("runId").GetGuid();
        foreach (var (run, how) in new[] { (bySchedule, "schedule"), (byWebhook, "webhook") })
        {
            (await runner.EndedRunAsync(run, TimeSpan.FromMinutes(2))).GetProperty("state").GetString().ShouldBe("Succeeded");
            (await runner.LogAsync(run)).ShouldContain(l => l == $"started by {how}");
        }
    }

    [Fact]
    public async Task A_job_whose_runner_vanishes_is_retried_on_another_runner()
    {
        using var docker = await DockerTests.DockerAsync();
        await using var first = await TestRunner.CreateAsync(docker, Database, ("Queue:LeaseDuration", "00:00:10"));
        await first.RegisterAsync("first");
        await first.StartAsync();
        var run = await first.StartRunAsync(Slow);
        await TestRunner.EventuallyAsync(async () => (await first.LogAsync(run)).Contains("started", StringComparer.Ordinal), "The step did not start.");

        await first.Network.BlockAsync();
        first.Server.Clock.Advance(TimeSpan.FromMinutes(1));
        await TestRunner.EventuallyAsync(
            async () => (await first.GetAsync($"/api/v1/runs/{run}/jobs/build")).GetProperty("attempts").GetArrayLength() == 2,
            "The lost attempt was not given up.");
        await using var second = first.Another();
        await second.RegisterAsync("second");
        await second.StartAsync();

        (await first.EndedRunAsync(run, TimeSpan.FromMinutes(2))).GetProperty("state").GetString().ShouldBe("Succeeded");
        var attempts = (await first.GetAsync($"/api/v1/runs/{run}/jobs/build")).GetProperty("attempts");
        attempts[0].GetProperty("status").GetString().ShouldBe("Failed");
        attempts[1].GetProperty("status").GetString().ShouldBe("Succeeded");
        attempts[1].GetProperty("runnerName").GetString().ShouldBe("second");
    }

    /// <summary>The events of an attempt's live log until it ends: <c>chunk</c> for each chunk, then <c>end</c>.</summary>
    private static async Task<List<string>> FollowAsync(TestRunner runner, Guid run, string job)
    {
        string? attempt = null;
        await TestRunner.EventuallyAsync(
            async () => (attempt = (await runner.GetAsync($"/api/v1/runs/{run}/jobs/{job}")).GetProperty("attempts").EnumerateArray().Select(a => a.GetProperty("id").GetString()).FirstOrDefault()) is not null,
            "The job got no attempt.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/v1/runs/{run}/attempts/{attempt}/log/stream", UriKind.Relative));
        using var response = await runner.Admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Cancel);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Cancel));
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        patience.CancelAfter(TimeSpan.FromMinutes(3));
        var events = new List<string>();
        while (await reader.ReadLineAsync(patience.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                events.Add(line[7..]);
                if (string.Equals(line[7..], "end", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        return events;
    }

    private static async Task<Guid> RunOfAsync(TestRunner runner, Guid automation, string trigger)
    {
        JsonElement? found = null;
        await TestRunner.EventuallyAsync(
            async () => (found = (await runner.GetAsync($"/api/v1/automations/{automation}/runs")).GetProperty("items").EnumerateArray()
                .Cast<JsonElement?>().FirstOrDefault(r => string.Equals(r!.Value.GetProperty("trigger").GetString(), trigger, StringComparison.Ordinal))) is not null,
            $"No run started by {trigger}.");
        return found!.Value.GetProperty("id").GetGuid();
    }
}

public sealed class S2WalkThroughTestsOnSqlite() : S2WalkThroughTests(DatabaseProvider.Sqlite);

public sealed class S2WalkThroughTestsOnPostgres() : S2WalkThroughTests(DatabaseProvider.Postgres);
