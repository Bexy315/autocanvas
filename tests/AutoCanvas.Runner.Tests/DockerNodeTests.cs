namespace AutoCanvas.Runner.Tests;

/// <summary>
/// Services, checkouts and <c>docker_build</c> on a real Docker engine, skipped without one as in
/// <see cref="DockerTests"/>. The checkout clones a small public repository from GitHub.
/// </summary>
public sealed class DockerNodeTests
{
    [Fact]
    public async Task A_job_reaches_its_service_and_builds_an_image_from_a_checkout()
    {
        using var docker = await DockerTests.DockerAsync();
        await using var runner = await TestRunner.CreateAsync(docker);
        await runner.RegisterAsync();
        await runner.StartAsync();

        var run = await runner.StartRunAsync($$$"""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                image: {{{DockerTests.Image}}}
                checkout:
                  - repository: https://github.com/octocat/Hello-World.git
                    path: hello
                services:
                  web:
                    image: nginx:1.27-alpine
                    ready: wget -q -O /dev/null http://localhost
                steps:
                  - shell: sh
                    run: |
                      wget -q -O - http://web | grep -o 'Welcome to nginx'
                      test -f hello/README
                      mkdir image
                      printf 'FROM {{{DockerTests.Image}}}\nRUN echo built > /built\n' > image/Dockerfile
                  - id: build
                    type: docker_build
                    with:
                      image: autocanvas-test/hello
                      context: image
                outputs:
                  digest: ${{ steps.build.outputs.digest }}
            """);

        // Pulling nginx, git and BuildKit and building take a while on a fresh machine.
        var ended = await runner.EndedRunAsync(run, TimeSpan.FromMinutes(3));
        var log = await runner.LogAsync(run);
        ended.GetProperty("state").GetString().ShouldBe("Succeeded", string.Join(Environment.NewLine, log));
        log.ShouldContain(l => l == "Welcome to nginx");
        log.ShouldContain(l => l == "Service web is ready.");
        var job = await runner.GetAsync($"/api/v1/runs/{run}/jobs/build");
        job.GetProperty("outputs").GetProperty("digest").GetString()!.ShouldStartWith("sha256:");
        Directory.EnumerateDirectories(Path.Combine(runner.DataDirectory, "work")).ShouldBeEmpty();
        (await docker.RemoveLabelledAsync(Execution.ContainerLabels.Job, job.GetProperty("attempts")[0].GetProperty("id").GetString()!, TestContext.Current.CancellationToken)).ShouldBe(0);
    }
}
