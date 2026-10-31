using AutoCanvas.Server.Runners;
using Microsoft.Extensions.Configuration;

namespace AutoCanvas.Server.Tests.Runners;

public sealed class RunnerHostingTests
{
    [Fact]
    public void The_api_and_the_runners_get_endpoints_of_their_own_and_only_the_runners_speak_http2_alone()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["urls"] = "http://+:8080; https://+:8443",
            ["Runners:Urls"] = "http://+:5081",
        });

        RunnerHosting.AddKestrelEndpoints(configuration);

        configuration["Kestrel:Endpoints:Api0:Url"].ShouldBe("http://+:8080");
        configuration["Kestrel:Endpoints:Api1:Url"].ShouldBe("https://+:8443");
        configuration["Kestrel:Endpoints:Api0:Protocols"].ShouldBeNull();
        configuration["Kestrel:Endpoints:Runners0:Url"].ShouldBe("http://+:5081");
        configuration["Kestrel:Endpoints:Runners0:Protocols"].ShouldBe("Http2");
        configuration["urls"].ShouldBeNull();
    }

    [Fact]
    public void Without_settings_the_defaults_apply()
    {
        var configuration = new ConfigurationManager();

        RunnerHosting.AddKestrelEndpoints(configuration);

        configuration["Kestrel:Endpoints:Api0:Url"].ShouldBe(RunnerHosting.DefaultApiUrls);
        configuration["Kestrel:Endpoints:Runners0:Url"].ShouldBe(RunnerHosting.DefaultUrls);
    }

    [Fact]
    public void Endpoints_configured_by_hand_are_left_alone()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["urls"] = "http://+:8080",
            ["Kestrel:Endpoints:Mine:Url"] = "http://+:9000",
        });

        RunnerHosting.AddKestrelEndpoints(configuration);

        configuration["Kestrel:Endpoints:Runners0:Url"].ShouldBeNull();
        configuration["urls"].ShouldBe("http://+:8080");
    }
}
