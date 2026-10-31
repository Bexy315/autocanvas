using AutoCanvas.Server.Configuration;
using AutoCanvas.Server.Hosting;
using AutoCanvas.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("AUTOCANVAS_Server__PublicUrl", "Server:PublicUrl")]
    [InlineData("autocanvas_server__publicurl", "Server:PublicUrl")]
    [InlineData("AUTOCANVAS_A__B__C", "A:B:C")]
    [InlineData("AUTOCANVAS_URLS", "urls")]
    [InlineData("AUTOCANVAS_MASTER_KEY", "Secrets:MasterKey")]
    [InlineData("AUTOCANVAS_MASTER_KEY_FILE", "Secrets:MasterKeyFile")]
    public void Variables_map_to_configuration_keys(string variable, string key) =>
        Build((variable, "value"))[key].ShouldBe("value");

    [Theory]
    [InlineData("OTHER_Server__PublicUrl")]
    [InlineData("AUTOCANVAS_")]
    public void Other_variables_are_ignored(string variable) =>
        Build((variable, "value")).AsEnumerable().Where(pair => pair.Value is not null).ShouldBeEmpty();

    [Fact]
    public void The_long_form_wins_over_an_alias()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AUTOCANVAS_MASTER_KEY"] = "Secrets:MasterKey" };
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AUTOCANVAS_MASTER_KEY"] = "short",
            ["AUTOCANVAS_Secrets__MasterKey"] = "long",
        };

        var configuration = new ConfigurationBuilder().Add(new AutoCanvasEnvironmentSource(environment, aliases)).Build();

        configuration["Secrets:MasterKey"].ShouldBe("long");
    }

    [Fact]
    public void Command_line_arguments_win_over_environment_variables()
    {
        var builder = new ConfigurationBuilder().AddCommandLine(["--Server:PublicUrl=https://cli"]);
        ServerBuilderExtensions.AddAutoCanvasEnvironment(builder, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AUTOCANVAS_Server__PublicUrl"] = "https://env",
        });

        builder.Build()["Server:PublicUrl"].ShouldBe("https://cli");
    }

    [Fact]
    public void The_process_environment_can_be_read() =>
        AutoCanvasEnvironmentSource.ReadProcessEnvironment().ShouldNotBeEmpty();

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://autocanvas.example.org", true)]
    [InlineData("http://localhost:5080/ci", true)]
    [InlineData("autocanvas.example.org", false)]
    [InlineData("ftp://autocanvas.example.org", false)]
    [InlineData("/relative", false)]
    public void Public_url_must_be_absolute_http(string? url, bool valid) =>
        new ServerOptionsValidator().Validate(null, new ServerOptions { PublicUrl = url }).Succeeded.ShouldBe(valid);

    [Fact]
    public async Task Invalid_settings_stop_the_server_from_starting()
    {
        await using var database = SqliteTestDatabase.Create();

        var message = await ServerStart.FailsAsync<OptionsValidationException>(database, ("Server:PublicUrl", "not-a-url"));

        message.ShouldContain("Server:PublicUrl must be an absolute http or https URL");
    }

    private static IConfigurationRoot Build(params (string Variable, string Value)[] environment)
    {
        var builder = new ConfigurationBuilder();
        ServerBuilderExtensions.AddAutoCanvasEnvironment(
            builder,
            environment.ToDictionary(e => e.Variable, e => (string?)e.Value, StringComparer.Ordinal));
        return builder.Build();
    }
}
