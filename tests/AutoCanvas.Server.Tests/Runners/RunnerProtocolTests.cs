using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.TestSupport;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Server.Tests.Runners;

/// <summary>The server side of runner protocol v1, driven by a runner played by the test.</summary>
public abstract class RunnerProtocolTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private const string OneJob = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            runs_on: linux
            steps:
              - run: echo build
        """;

    private const string WithReports = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            runs_on: linux
            steps:
              - run: dotnet test
            reports:
              - format: junit
                path: reports/*.xml
                name: Unit tests
        """;

    private const string TwoJobs = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            runs_on: linux
            steps:
              - run: echo build
          lint:
            runs_on: linux
            steps:
              - run: echo lint
        """;

    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public async Task A_runner_registers_once_and_connects_with_its_credential()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var token = await TokenAsync(admin);

        var registered = await client.RegisterAsync(token, "build-1");
        var again = await Should.ThrowAsync<RpcException>(client.RegisterAsync(token, "build-2"));
        var unknown = await Should.ThrowAsync<RpcException>(client.RegisterAsync("acrt_unknown", "build-3"));
        var (stream, welcome) = await client.ConnectAsync(registered.Credential);
        using (stream)
        {
            welcome.ProtocolVersion.ShouldBe("1.0");
            welcome.RunnerId.ShouldBe(registered.RunnerId);
            welcome.HeartbeatInterval.ToTimeSpan().ShouldBe(TimeSpan.FromSeconds(1));
            welcome.LeaseDuration.ToTimeSpan().ShouldBe(TimeSpan.FromMinutes(1));
        }

        again.StatusCode.ShouldBe(StatusCode.AlreadyExists);
        unknown.StatusCode.ShouldBe(StatusCode.Unauthenticated);
        var runners = await (await admin.GetAsync(Uri("/api/v1/runners"), Cancel)).JsonAsync();
        var runner = runners.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        runner.GetProperty("name").GetString().ShouldBe("build-1");
        runner.GetProperty("isOnline").GetBoolean().ShouldBeTrue();
        runner.GetProperty("capabilities").GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ShouldBe(["linux"]);
    }

    [Fact]
    public async Task Registration_refuses_capabilities_without_an_executor()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var token = await TokenAsync(await factory.SignedInAsync("root", isAdmin: true));
        var capabilities = ProtocolClient.Capabilities();
        capabilities.Executor = Executor.Unspecified;

        (await Should.ThrowAsync<RpcException>(client.RegisterAsync(token, "odd", capabilities))).StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task Runner_credentials_and_user_credentials_never_work_on_the_other_side()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var credential = await RegisterAsync(client, admin, "build-1");
        var userToken = (await (await admin.PostAsJsonAsync("/api/v1/me/tokens", new { name = "script" }, Cancel)).JsonAsync()).GetProperty("token").GetString();

        using var withUserToken = await client.OpenAsync(userToken);
        using var withoutCredential = await client.OpenAsync(null);
        using var api = factory.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        (await withUserToken.EndAsync()).ShouldBe(StatusCode.Unauthenticated);
        (await withoutCredential.EndAsync()).ShouldBe(StatusCode.Unauthenticated);
        (await api.GetAsync(Uri("/api/v1/me"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_stream_must_start_with_a_hello_in_protocol_version_one()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var credential = await RegisterAsync(client, await factory.SignedInAsync("root", isAdmin: true), "build-1");
        var future = ProtocolClient.Hello();
        future.ProtocolVersion = "2.0";

        using var newer = await client.OpenAsync(credential, future);
        using var heartbeatFirst = await client.OpenWithAsync(credential, new ConnectRequest { Heartbeat = new Heartbeat() });

        (await newer.EndAsync()).ShouldBe(StatusCode.FailedPrecondition);
        (await heartbeatFirst.EndAsync()).ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task Queued_jobs_are_offered_to_runners_that_fit_and_accepted_ones_run()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var credential = await RegisterAsync(client, admin, "build-1");
        var windows = await RegisterAsync(client, admin, "build-2", ProtocolClient.Capabilities(labels: "windows"));
        var (stream, _) = await client.ConnectAsync(credential);
        var (other, _) = await client.ConnectAsync(windows, ProtocolClient.Hello(ProtocolClient.Capabilities(labels: "windows")));
        using (stream)
        using (other)
        {
            var run = await StartRunAsync(admin, OneJob);
            var offer = (await stream.ReadAsync()).JobOffer.Job;
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = offer.JobId } });
            await stream.HeartbeatAsync(offer.JobId);

            offer.RunId.ShouldBe(run.ToString());
            offer.JobKey.ShouldBe("build");
            offer.Attempt.ShouldBe(1u);
            offer.AutomationName.ShouldBe("build");
            offer.Timeout.ToTimeSpan().ShouldBe(TimeSpan.FromMinutes(60));
            (await other.TryReadAsync(Quiet)).ShouldBeNull();
            await EventuallyAsync(factory, Guid.Parse(offer.JobId), a => a.Status == AttemptStatus.Running);
        }
    }

    [Fact]
    public async Task A_runner_takes_no_more_jobs_than_it_runs_at_once_and_rejected_ones_go_back()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, admin, "build-1"));
        using (stream)
        {
            await StartRunAsync(admin, TwoJobs);
            var first = (await stream.ReadAsync()).JobOffer.Job;
            (await stream.TryReadAsync(Quiet)).ShouldBeNull();
            await stream.SendAsync(new ConnectRequest { JobRejected = new JobRejected { JobId = first.JobId, Reason = "at capacity" } });
            var second = (await stream.ReadAsync()).JobOffer.Job;

            second.JobKey.ShouldBeOneOf("build", "lint");
            await EventuallyAsync(factory, Guid.Parse(second.JobId), a => a.Status == AttemptStatus.Offered);
        }
    }

    [Fact]
    public async Task Paused_and_draining_runners_are_offered_nothing()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var registered = await client.RegisterAsync(await TokenAsync(admin), "build-1");
        (await admin.PatchAsJsonAsync($"/api/v1/runners/{registered.RunnerId}", new { isPaused = true }, Cancel)).EnsureSuccessStatusCode();
        var (stream, _) = await client.ConnectAsync(registered.Credential);
        using (stream)
        {
            await StartRunAsync(admin, OneJob);
            (await stream.TryReadAsync(Quiet)).ShouldBeNull();

            (await admin.PatchAsJsonAsync($"/api/v1/runners/{registered.RunnerId}", new { isPaused = false }, Cancel)).EnsureSuccessStatusCode();
            var offer = (await stream.ReadAsync()).JobOffer.Job;
            await stream.SendAsync(new ConnectRequest { Draining = new Draining() });
            await stream.SendAsync(new ConnectRequest { JobRejected = new JobRejected { JobId = offer.JobId, Reason = "shutting down" } });

            // A rejected offer would come back at once; while draining it does not.
            (await stream.TryReadAsync(Quiet)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task After_a_reconnect_jobs_the_runner_still_holds_continue_and_others_are_cancelled()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var credential = await RegisterAsync(client, admin, "build-1");
        string held;
        var (first, _) = await client.ConnectAsync(credential);
        using (first)
        {
            await StartRunAsync(admin, OneJob);
            held = (await first.ReadAsync()).JobOffer.Job.JobId;
            await first.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = held } });
            await EventuallyAsync(factory, Guid.Parse(held), a => a.Status == AttemptStatus.Running);
        }

        var lost = Guid.CreateVersion7().ToString();
        factory.Clock.Advance(TimeSpan.FromSeconds(50));
        var (second, _) = await client.ConnectAsync(credential, ProtocolClient.Hello(null, held, lost, "not-an-id"));
        using (second)
        {
            var cancelled = new[] { (await second.ReadAsync()).CancelJob, (await second.ReadAsync()).CancelJob };

            cancelled.Select(c => c.JobId).ShouldBe([lost, "not-an-id"], ignoreOrder: true);
            cancelled[0].Reason.ShouldContain("given up");
            (await second.TryReadAsync(Quiet)).ShouldBeNull();
            var attempt = await AttemptAsync(factory, Guid.Parse(held));
            attempt.LeaseExpiresAt.ShouldBe(factory.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task Heartbeats_renew_leases_and_cancel_jobs_the_runner_no_longer_holds()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, admin, "build-1"));
        using (stream)
        {
            await StartRunAsync(admin, OneJob);
            var job = (await stream.ReadAsync()).JobOffer.Job.JobId;
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });
            var stray = Guid.CreateVersion7().ToString();
            factory.Clock.Advance(TimeSpan.FromSeconds(30));
            await stream.HeartbeatAsync(job, stray);

            var cancel = (await stream.ReadAsync()).CancelJob;
            cancel.JobId.ShouldBe(stray);
            await EventuallyAsync(factory, Guid.Parse(job), a => a.LeaseExpiresAt == factory.Clock.GetUtcNow() + TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task Accepting_an_offer_that_ran_out_cancels_it()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, admin, "build-1"));
        using (stream)
        {
            await StartRunAsync(admin, OneJob);
            var job = (await stream.ReadAsync()).JobOffer.Job.JobId;
            await ReturnToQueueAsync(factory, Guid.Parse(job));
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });

            var cancel = (await stream.ReadAsync()).CancelJob;
            cancel.JobId.ShouldBe(job);
            cancel.Reason.ShouldContain("withdrawn");
        }
    }

    [Fact]
    public async Task Jobs_the_server_cannot_offer_fail_with_the_reason_instead()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, admin, "build-1"));
        using (stream)
        {
            var run = await StartRunAsync(admin, """
                version: 1
                triggers:
                  manual: true
                jobs:
                  test:
                    runs_on: linux
                    checkout:
                      - repository: self
                    steps:
                      - run: echo test
                """);

            var until = DateTimeOffset.UtcNow + RunnerStream.Patience;
            while (!string.Equals((await (await admin.GetAsync(Uri($"/api/v1/runs/{run}"), Cancel)).JsonAsync()).GetProperty("state").GetString(), "Failed", StringComparison.Ordinal))
            {
                DateTimeOffset.UtcNow.ShouldBeLessThan(until, "The run did not fail in time.");
                await Task.Delay(50, Cancel);
            }

            (await stream.TryReadAsync(Quiet)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_deleted_runner_is_told_goodbye_for_good_and_refused_afterwards()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var registered = await client.RegisterAsync(await TokenAsync(admin), "doomed");
        var (stream, _) = await client.ConnectAsync(registered.Credential);
        using (stream)
        {
            (await admin.DeleteAsync(Uri($"/api/v1/runners/{registered.RunnerId}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var goodbye = (await stream.ReadAsync()).Goodbye;
            goodbye.Reconnect.ShouldBeFalse();
            (await stream.EndAsync()).ShouldBe(StatusCode.OK);
        }

        using var again = await client.OpenAsync(registered.Credential);
        (await again.EndAsync()).ShouldBe(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task Runners_are_told_to_reconnect_later_when_the_server_stops()
    {
        var factory = Factory();
        using var client = new ProtocolClient(factory);
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, await factory.SignedInAsync("root", isAdmin: true), "build-1"));
        using (stream)
        {
            // The server waits for its runner sessions to say goodbye before it stops.
            var stopping = factory.DisposeAsync().AsTask();
            var goodbye = (await stream.ReadAsync()).Goodbye;
            await stopping;

            goodbye.Reconnect.ShouldBeTrue();
            goodbye.ReconnectAfter.ToTimeSpan().ShouldBe(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task An_upload_needs_a_credential_and_a_header()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var credential = await RegisterAsync(client, await factory.SignedInAsync("root", isAdmin: true), "build-1");
        var service = new RunnerService.RunnerServiceClient(client.Channel);

        var empty = await Should.ThrowAsync<RpcException>(UploadAsync(service, credential, null));
        var anonymous = await Should.ThrowAsync<RpcException>(UploadAsync(service, "acr_unknown", Header(Guid.CreateVersion7().ToString())));
        var nameless = await Should.ThrowAsync<RpcException>(UploadAsync(service, credential, new ReportHeader { JobId = "not-an-id", Format = ReportFormat.Junit }));

        empty.StatusCode.ShouldBe(StatusCode.InvalidArgument);
        anonymous.StatusCode.ShouldBe(StatusCode.Unauthenticated);
        nameless.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task A_job_runs_step_by_step_and_its_result_decides_the_run()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, admin, "build-1"));
        using (stream)
        {
            var run = await StartRunAsync(admin, """
                version: 1
                triggers:
                  manual: true
                jobs:
                  build:
                    runs_on: linux
                    steps:
                      - id: version
                        run: echo tag=v7 >> $AUTOCANVAS_OUTPUT
                      - run: echo ${{ steps.version.outputs.tag }}
                """);
            var job = (await stream.ReadAsync()).JobOffer.Job.JobId;
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });
            await stream.SendAsync(new ConnectRequest { JobPrepared = new JobPrepared { JobId = job } });
            var first = (await stream.ReadAsync()).RunStep;
            await stream.SendAsync(new ConnectRequest { StepStarted = new StepStarted { JobId = job, StepIndex = 0 } });
            await stream.SendAsync(Completed(job, 0, Protocol.Runner.V1.StepOutcome.Succeeded, ("tag", "v7")));
            var second = (await stream.ReadAsync()).RunStep;
            await stream.SendAsync(Completed(job, 1, Protocol.Runner.V1.StepOutcome.Succeeded));
            var complete = (await stream.ReadAsync()).CompleteJob;
            await stream.SendAsync(new ConnectRequest { JobFinished = new JobFinished { JobId = job } });

            first.JobId.ShouldBe(job);
            first.Step.Index.ShouldBe(0u);
            first.Step.Id.ShouldBe("version");
            first.Step.Script.Shell.ShouldBe("bash");
            second.Step.Index.ShouldBe(1u);
            second.Step.Script.Script.ShouldBe("echo v7");
            complete.JobId.ShouldBe(job);
            await RunStateAsync(admin, run, "Succeeded");
        }
    }

    [Fact]
    public async Task Cancelling_a_run_sends_cancel_job_to_the_runner_of_its_running_job()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, admin, "build-1"));
        using (stream)
        {
            var run = await StartRunAsync(admin, OneJob);
            var job = (await stream.ReadAsync()).JobOffer.Job.JobId;
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });
            await stream.SendAsync(new ConnectRequest { JobPrepared = new JobPrepared { JobId = job } });
            (await stream.ReadAsync()).MessageCase.ShouldBe(ConnectResponse.MessageOneofCase.RunStep);

            (await admin.PostAsync(Uri($"/api/v1/runs/{run}/cancel"), null, Cancel)).EnsureSuccessStatusCode();
            var cancel = (await stream.ReadAsync()).CancelJob;
            await stream.SendAsync(Completed(job, 0, Protocol.Runner.V1.StepOutcome.Cancelled));
            await stream.SendAsync(new ConnectRequest { JobFinished = new JobFinished { JobId = job } });

            cancel.JobId.ShouldBe(job);
            cancel.Reason.ShouldBe("The run was cancelled.");
            await EventuallyAsync(factory, Guid.Parse(job), a => a.Status == AttemptStatus.Cancelled);
            (await stream.TryReadAsync(Quiet)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task After_a_reconnect_the_step_the_runner_may_have_missed_is_sent_again()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var credential = await RegisterAsync(client, admin, "build-1");
        string job;
        var (first, _) = await client.ConnectAsync(credential);
        using (first)
        {
            await StartRunAsync(admin, OneJob);
            job = (await first.ReadAsync()).JobOffer.Job.JobId;
            await first.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });
            await first.SendAsync(new ConnectRequest { JobPrepared = new JobPrepared { JobId = job } });
            (await first.ReadAsync()).MessageCase.ShouldBe(ConnectResponse.MessageOneofCase.RunStep);
        }

        var (second, _) = await client.ConnectAsync(credential, ProtocolClient.Hello(null, job));
        using (second)
        {
            var resent = (await second.ReadAsync()).RunStep;

            resent.JobId.ShouldBe(job);
            resent.Step.Index.ShouldBe(0u);
        }
    }

    [Fact]
    public async Task Logs_arrive_masked_once_and_are_read_and_followed_live()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var folder = await FolderAsync(admin);
        (await admin.PutAsJsonAsync($"/api/v1/folders/{folder}/secrets/TOKEN", new { value = "s3cret-token" }, Cancel)).EnsureSuccessStatusCode();
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, admin, "build-1"));
        using (stream)
        {
            var run = await StartRunAsync(admin, OneJob, folder);
            var job = (await stream.ReadAsync()).JobOffer.Job.JobId;
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });
            await stream.SendAsync(Log(job, 0, null, "Pulling image"));
            await stream.SendAsync(Log(job, 0, null, "Pulling image"));
            await stream.SendAsync(new ConnectRequest { JobPrepared = new JobPrepared { JobId = job } });
            (await stream.ReadAsync()).MessageCase.ShouldBe(ConnectResponse.MessageOneofCase.RunStep);

            using var follow = factory.CreateApiClient();
            await follow.SignInAsync("root");
            using var request = new HttpRequestMessage(HttpMethod.Get, Uri($"/api/v1/runs/{run}/attempts/{job}/log/stream"));
            using var response = await follow.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Cancel);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
            using var events = new StreamReader(await response.Content.ReadAsStreamAsync(Cancel));
            var first = await EventAsync(events);

            await stream.SendAsync(Log(job, 1, 0, "the token is s3cret-token"));
            var second = await EventAsync(events);
            await stream.SendAsync(Completed(job, 0, Protocol.Runner.V1.StepOutcome.Succeeded));
            (await stream.ReadAsync()).MessageCase.ShouldBe(ConnectResponse.MessageOneofCase.CompleteJob);
            await stream.SendAsync(new ConnectRequest { JobFinished = new JobFinished { JobId = job } });
            var end = await EventAsync(events);

            first.Event.ShouldBe("chunk");
            first.Id.ShouldBe("0");
            first.Data.GetProperty("lines")[0].GetProperty("text").GetString().ShouldBe("Pulling image");
            second.Id.ShouldBe("1");
            second.Data.GetProperty("stepIndex").GetInt32().ShouldBe(0);
            second.Data.GetProperty("lines")[0].GetProperty("text").GetString().ShouldBe("the token is ***");
            end.Event.ShouldBe("end");
            await ShouldBeStoredAsync(admin, run, job);
        }
    }

    /// <summary>What the logs test sent is readable as a page and the job shows its attempt.</summary>
    private static async Task ShouldBeStoredAsync(HttpClient admin, Guid run, string job)
    {
        var page = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/attempts/{job}/log"), Cancel)).JsonAsync();
        page.GetProperty("chunks").GetArrayLength().ShouldBe(2);
        page.GetProperty("isComplete").GetBoolean().ShouldBeTrue();
        page.GetProperty("chunks")[1].GetProperty("lines")[0].GetProperty("source").GetString().ShouldBe("Stdout");
        var detail = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/jobs/build"), Cancel)).JsonAsync();
        detail.GetProperty("status").GetString().ShouldBe("Succeeded");
        var attempt = detail.GetProperty("attempts").EnumerateArray().ShouldHaveSingleItem();
        attempt.GetProperty("id").GetString().ShouldBe(job);
        attempt.GetProperty("runnerName").GetString().ShouldBe("build-1");
        attempt.GetProperty("steps")[0].GetProperty("status").GetString().ShouldBe("Succeeded");
    }

    [Fact]
    public async Task Following_a_log_starts_after_the_last_event_the_reader_saw()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var (stream, _) = await client.ConnectAsync(await RegisterAsync(client, admin, "build-1"));
        using (stream)
        {
            var run = await StartRunAsync(admin, OneJob);
            var job = (await stream.ReadAsync()).JobOffer.Job.JobId;
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });
            await stream.SendAsync(Log(job, 0, null, "zero"));
            await stream.SendAsync(Log(job, 1, null, "one"));
            await EventuallyAsync(factory, Guid.Parse(job), a => a.LogBytes == 7);

            using var request = new HttpRequestMessage(HttpMethod.Get, Uri($"/api/v1/runs/{run}/attempts/{job}/log/stream"));
            request.Headers.Add("Last-Event-ID", "0");
            using var response = await admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Cancel);
            using var events = new StreamReader(await response.Content.ReadAsStreamAsync(Cancel));

            (await EventAsync(events)).Id.ShouldBe("1");
            (await admin.GetAsync(Uri($"/api/v1/runs/{run}/attempts/{Guid.CreateVersion7()}/log/stream"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await admin.GetAsync(Uri($"/api/v1/runs/{run}/attempts/{job}/log?limit=5000"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await admin.GetAsync(Uri($"/api/v1/runs/{run}/jobs/nope"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Reports_are_uploaded_in_parts_once_and_read_with_the_run()
    {
        await using var factory = Factory();
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var credential = await RegisterAsync(client, admin, "build-1");
        var (stream, _) = await client.ConnectAsync(credential);
        using (stream)
        {
            var run = await StartRunAsync(admin, WithReports);
            var job = (await stream.ReadAsync()).JobOffer.Job.JobId;
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });
            await EventuallyAsync(factory, Guid.Parse(job), a => a.Status == AttemptStatus.Running);
            var service = new RunnerService.RunnerServiceClient(client.Channel);
            const string Xml = """<testsuite name="s"><testcase classname="C" name="a"/><testcase classname="C" name="b"><failure message="no"/></testcase></testsuite>""";

            var id = await UploadAsync(service, credential, Header(job), Xml[..20], Xml[20..60], Xml[60..]);
            var again = await UploadAsync(service, credential, Header(job), Xml);
            var withoutHeader = await Should.ThrowAsync<RpcException>(UploadAsync(service, credential, null, Xml));
            var undeclared = await Should.ThrowAsync<RpcException>(UploadAsync(service, credential, Header(job, "Other"), Xml));

            await stream.SendAsync(new ConnectRequest { JobPrepared = new JobPrepared { JobId = job } });
            (await stream.ReadAsync()).MessageCase.ShouldBe(ConnectResponse.MessageOneofCase.RunStep);
            await stream.SendAsync(Completed(job, 0, Protocol.Runner.V1.StepOutcome.Succeeded));
            (await stream.ReadAsync()).MessageCase.ShouldBe(ConnectResponse.MessageOneofCase.CompleteJob);
            await stream.SendAsync(new ConnectRequest { JobFinished = new JobFinished { JobId = job } });
            await EventuallyAsync(factory, Guid.Parse(job), a => a.Status == AttemptStatus.Succeeded);
            var late = await Should.ThrowAsync<RpcException>(UploadAsync(service, credential, Header(job, path: "reports/late.xml"), Xml));

            again.ShouldBe(id);
            withoutHeader.StatusCode.ShouldBe(StatusCode.InvalidArgument);
            undeclared.StatusCode.ShouldBe(StatusCode.InvalidArgument);
            late.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
            var reports = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/reports"), Cancel)).JsonAsync();
            reports.GetProperty("tests").GetProperty("failed").GetInt32().ShouldBe(1);
            var report = reports.GetProperty("reports").EnumerateArray().ShouldHaveSingleItem();
            report.GetProperty("id").GetString().ShouldBe(id);
            report.GetProperty("path").GetString().ShouldBe("reports/junit.xml");
            var detail = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/jobs/build"), Cancel)).JsonAsync();
            detail.GetProperty("attempts")[0].GetProperty("reports")[0].GetProperty("tests").GetProperty("total").GetInt32().ShouldBe(2);
            await ShouldReadReportAsync(admin, run, id);
        }
    }

    /// <summary>What the reports test uploaded is readable test by test, and in the history of its automation.</summary>
    private static async Task ShouldReadReportAsync(HttpClient admin, Guid run, string id)
    {
        var report = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/reports/{id}"), Cancel)).JsonAsync();
        var failed = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/reports/{id}/tests?outcome=failed"), Cancel)).JsonAsync();
        var page = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/reports/{id}/tests?limit=1"), Cancel)).JsonAsync();
        var classes = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/reports/{id}/coverage/classes"), Cancel)).JsonAsync();
        var automation = (await (await admin.GetAsync(Uri($"/api/v1/runs/{run}"), Cancel)).JsonAsync()).GetProperty("automationId").GetString();
        var history = await (await admin.GetAsync(Uri($"/api/v1/automations/{automation}/tests/history?name=C.b"), Cancel)).JsonAsync();

        report.GetProperty("report").GetProperty("name").GetString().ShouldBe("Unit tests");
        report.GetProperty("packages").GetArrayLength().ShouldBe(0);
        var test = failed.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        test.GetProperty("fullName").GetString().ShouldBe("C.b");
        test.GetProperty("outcome").GetString().ShouldBe("Failed");
        page.GetProperty("next").GetString().ShouldBe("0");
        classes.GetProperty("items").GetArrayLength().ShouldBe(0);
        history.GetProperty("failed").GetInt32().ShouldBe(1);
        history.GetProperty("entries")[0].GetProperty("runNumber").GetInt32().ShouldBe(1);
        (await admin.GetAsync(Uri($"/api/v1/runs/{run}/reports/{id}/tests?outcome=green"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync(Uri($"/api/v1/runs/{run}/reports/{Guid.CreateVersion7()}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync(Uri($"/api/v1/automations/{automation}/tests/history"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_report_larger_than_the_limit_is_refused()
    {
        await using var factory = Factory().With("Reports:MaxBytesPerFile", "1048576");
        using var client = new ProtocolClient(factory);
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var credential = await RegisterAsync(client, admin, "build-1");
        var (stream, _) = await client.ConnectAsync(credential);
        using (stream)
        {
            var run = await StartRunAsync(admin, WithReports);
            var job = (await stream.ReadAsync()).JobOffer.Job.JobId;
            await stream.SendAsync(new ConnectRequest { JobAccepted = new JobAccepted { JobId = job } });
            await EventuallyAsync(factory, Guid.Parse(job), a => a.Status == AttemptStatus.Running);
            var service = new RunnerService.RunnerServiceClient(client.Channel);
            var part = "<testsuite>" + new string(' ', 600 * 1024);

            var refused = await Should.ThrowAsync<RpcException>(UploadAsync(service, credential, Header(job), part, new string(' ', 600 * 1024)));

            refused.StatusCode.ShouldBe(StatusCode.ResourceExhausted);
            var reports = await (await admin.GetAsync(Uri($"/api/v1/runs/{run}/reports"), Cancel)).JsonAsync();
            reports.GetProperty("reports").GetArrayLength().ShouldBe(0);
        }
    }

    private static ReportHeader Header(string job, string name = "Unit tests", string path = "reports/junit.xml") =>
        new() { JobId = job, Format = ReportFormat.Junit, Name = name, Path = path, Size = 100 };

    /// <summary>Uploads a report as a runner does: the header, then the content in the given parts.</summary>
    private static async Task<string> UploadAsync(RunnerService.RunnerServiceClient service, string credential, ReportHeader? header, params string[] parts)
    {
        using var call = service.UploadReport(ProtocolClient.Authorization(credential), cancellationToken: Cancel);
        try
        {
            if (header is not null)
            {
                await call.RequestStream.WriteAsync(new UploadReportRequest { Header = header }, Cancel);
            }

            foreach (var part in parts)
            {
                await call.RequestStream.WriteAsync(new UploadReportRequest { Content = ByteString.CopyFromUtf8(part) }, Cancel);
            }

            await call.RequestStream.CompleteAsync();
        }
        catch (RpcException)
        {
            // The server answered before the upload was complete; the response says why.
        }

        return (await call.ResponseAsync).ReportId;
    }

    private static ConnectRequest Log(string job, ulong sequence, uint? step, params string[] lines)
    {
        var chunk = new Protocol.Runner.V1.LogChunk { JobId = job, Sequence = sequence };
        if (step is { } index)
        {
            chunk.StepIndex = index;
        }
        else
        {
            chunk.JobSetup = true;
        }

        foreach (var line in lines)
        {
            chunk.Lines.Add(new Protocol.Runner.V1.LogLine { Text = line, Stream = Protocol.Runner.V1.LogStream.Stdout, Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch) });
        }

        return new ConnectRequest { LogChunk = chunk };
    }

    /// <summary>The next Server-Sent Event, skipping comments.</summary>
    private static async Task<(string? Id, string? Event, JsonElement Data)> EventAsync(StreamReader reader)
    {
        string? id = null, name = null, data = null;
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        patience.CancelAfter(RunnerStream.Patience);
        while (await reader.ReadLineAsync(patience.Token) is { } line)
        {
            if (line.Length == 0 && data is not null)
            {
                return (id, name, JsonDocument.Parse(data).RootElement.Clone());
            }

            if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                id = line[4..];
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line[6..];
            }
        }

        throw new ShouldAssertException("The event stream ended.");
    }

    private static async Task<Guid> FolderAsync(HttpClient client) =>
        Id(await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Team" + Guid.CreateVersion7().ToString("N")[^8..] }, Cancel)).JsonAsync());

    private static ConnectRequest Completed(string job, uint index, Protocol.Runner.V1.StepOutcome outcome, params (string Name, string Value)[] outputs)
    {
        var completed = new StepCompleted { JobId = job, StepIndex = index, Outcome = outcome };
        foreach (var (name, value) in outputs)
        {
            completed.Outputs[name] = value;
        }

        return new ConnectRequest { StepCompleted = completed };
    }

    private static async Task RunStateAsync(HttpClient client, Guid run, string state)
    {
        var until = DateTimeOffset.UtcNow + RunnerStream.Patience;
        while (!string.Equals((await (await client.GetAsync(Uri($"/api/v1/runs/{run}"), Cancel)).JsonAsync()).GetProperty("state").GetString(), state, StringComparison.Ordinal))
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(until, $"The run did not become {state} in time.");
            await Task.Delay(50, Cancel);
        }
    }

    private static async Task<string> TokenAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/runner-registration-tokens", new { description = "test" }, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("token").GetString()!;
    }

    private static async Task<string> RegisterAsync(ProtocolClient client, HttpClient admin, string name, Capabilities? capabilities = null) =>
        (await client.RegisterAsync(await TokenAsync(admin), name, capabilities)).Credential;

    private static async Task<Guid> StartRunAsync(HttpClient client, string yaml, Guid? inFolder = null)
    {
        var folder = inFolder ?? await FolderAsync(client);
        var automation = Id(await (await client.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "build", yaml }, Cancel)).JsonAsync());
        var started = await client.PostAsync(Uri($"/api/v1/automations/{automation}/runs"), null, Cancel);
        started.StatusCode.ShouldBe(HttpStatusCode.Created);
        return Id(await started.JsonAsync());
    }

    private static async Task<JobAttempt> AttemptAsync(AutoCanvasFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().JobAttempts.AsNoTracking().SingleAsync(a => a.Id == id, Cancel);
    }

    /// <summary>What the session does after a message is not acknowledged; wait until it shows in the database.</summary>
    private static async Task EventuallyAsync(AutoCanvasFactory factory, Guid attempt, Func<JobAttempt, bool> condition)
    {
        var until = DateTimeOffset.UtcNow + RunnerStream.Patience;
        while (!condition(await AttemptAsync(factory, attempt)))
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(until, "The attempt did not reach the expected state in time.");
            await Task.Delay(50, Cancel);
        }
    }

    /// <summary>What the sweeper does with an offer nobody accepted in time.</summary>
    private static async Task ReturnToQueueAsync(AutoCanvasFactory factory, Guid attempt)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().JobAttempts
            .Where(a => a.Id == attempt)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AttemptStatus.Queued).SetProperty(a => a.RunnerId, (Guid?)null).SetProperty(a => a.NotBefore, DateTimeOffset.MaxValue), Cancel);
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database).With("Runners:HeartbeatInterval", "00:00:01");
}

public sealed class RunnerProtocolTestsOnSqlite() : RunnerProtocolTests(DatabaseProvider.Sqlite);

public sealed class RunnerProtocolTestsOnPostgres() : RunnerProtocolTests(DatabaseProvider.Postgres);
