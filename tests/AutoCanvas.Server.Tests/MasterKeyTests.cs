using System.Net;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Tests;

public abstract class MasterKeyTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task The_server_does_not_start_without_a_usable_master_key()
    {
        var file = Path.Combine(Path.GetTempPath(), $"autocanvas-key-{Guid.CreateVersion7()}");
        await File.WriteAllTextAsync(file, MasterKey.Generate().Text + "\n", Cancel);
        try
        {
            StartFails<OptionsValidationException>(f => f.With("Secrets:MasterKey", null)).ShouldContain("No master key is configured");
            StartFails<OptionsValidationException>(f => f.With("Secrets:MasterKeyFile", file)).ShouldContain("not both");
            StartFails<OptionsValidationException>(f => f.With("Secrets:MasterKey", "AAEC")).ShouldContain("32 bytes");
            StartFails<OptionsValidationException>(f => f.With("Secrets:MasterKey", null).With("Secrets:MasterKeyFile", file + ".missing")).ShouldContain("cannot be read");

            await using var fromFile = Factory().With("Secrets:MasterKey", null).With("Secrets:MasterKeyFile", file);
            (await fromFile.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task The_server_refuses_a_different_key_than_the_database_was_set_up_with()
    {
        await using (var first = Factory())
        {
            _ = first.CreateClient();
        }

        var other = MasterKey.Generate();
        StartFails<InvalidOperationException>(f => f.With("Secrets:MasterKey", other.Text)).ShouldContain(other.Id);
    }

    [Fact]
    public async Task Generated_keys_are_usable_master_keys()
    {
        var (exitCode, output, _) = await RunAsync(string.Empty, "admin", "generate-master-key");

        exitCode.ShouldBe(0);
        MasterKey.Parse(output, out _).ShouldNotBeNull();
    }

    [Fact]
    public async Task Rotating_the_key_re_encrypts_secrets_for_the_next_start()
    {
        await using (var server = Factory())
        {
            var admin = await server.CreateUserAsync("root", isAdmin: true);
            await using var scope = server.Services.CreateAsyncScope();
            var set = await scope.ServiceProvider.GetRequiredService<SecretService>()
                .SetAsync(new Actor(admin.Id, IsAdmin: true), ConfigurationScope.Instance, "TOKEN", "s3cr3t-value", Cancel);
            set.Error.ShouldBeNull();
        }

        var next = MasterKey.Generate();
        var (missing, _, missingError) = await RunAsync(string.Empty, "admin", "rotate-master-key");
        var (bad, _, badError) = await RunAsync("nope\n", "admin", "rotate-master-key", "--new-key-stdin");
        var (exitCode, output, _) = await RunAsync(next.Text + "\n", "admin", "rotate-master-key", "--new-key-stdin");

        missing.ShouldBe(1);
        missingError.ShouldContain("--new-key-file");
        bad.ShouldBe(1);
        badError.ShouldContain("cannot be used");
        exitCode.ShouldBe(0);
        output.ShouldContain($"Re-encrypted 1 secret(s) with master key {next.Id}");
        StartFails<InvalidOperationException>(f => f).ShouldContain(next.Id);
        await using (var restarted = Factory().With("Secrets:MasterKey", next.Text))
        {
            await using var scope = restarted.Services.CreateAsyncScope();
            var secret = await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Secrets.SingleAsync(Cancel);
            _ = restarted.CreateClient();
            scope.ServiceProvider.GetRequiredService<SecretCipher>().Decrypt(secret.Scope, secret.NormalizedName, secret.Ciphertext).ShouldBe("s3cr3t-value");
        }
    }

    private string StartFails<TException>(Func<AutoCanvasFactory, AutoCanvasFactory> configure)
        where TException : Exception
    {
        using var factory = configure(Factory());
        return Should.Throw<TException>(() => factory.CreateClient()).Message;
    }

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string input, params string[] command) =>
        DatabaseTests.RunAsync(
            [.. command, $"--Database:Provider={Provider}", $"--Database:ConnectionString={Database.ConnectionString}", $"--Secrets:MasterKey={AutoCanvasFactory.MasterKey}"],
            input);
}

[Collection(CommandLineTests.Name)]
public sealed class MasterKeyTestsOnSqlite() : MasterKeyTests(DatabaseProvider.Sqlite);

[Collection(CommandLineTests.Name)]
public sealed class MasterKeyTestsOnPostgres() : MasterKeyTests(DatabaseProvider.Postgres);
