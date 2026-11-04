using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.TestSupport;

/// <summary>
/// A real Git server for tests: <c>git http-backend</c> (the smart HTTP protocol, as Git hosts speak
/// it) behind Kestrel on a free local port, with bare repositories in a temporary directory and,
/// if asked, Basic authentication. Repositories get commits through local pushes.
/// </summary>
public sealed class GitHttpServer : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly string root = Directory.CreateTempSubdirectory("autocanvas-git-").FullName;

    private GitHttpServer(WebApplication app, string address, (string User, string Password)? credential)
    {
        this.app = app;
        Address = new Uri(address);
        Credential = credential;
    }

    /// <summary>The server's address, such as <c>http://127.0.0.1:52341/</c>.</summary>
    public Uri Address { get; }

    /// <summary>The user and password every request must send, if any.</summary>
    public (string User, string Password)? Credential { get; }

    /// <summary>Starts a server; with a credential, it refuses requests without it.</summary>
    public static async Task<GitHttpServer> StartAsync((string User, string Password)? credential = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var host = builder.Build();
        GitHttpServer? server = null;
        host.Run(context => server!.ServeAsync(context));
        await host.StartAsync().ConfigureAwait(false);
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server = new GitHttpServer(host, address + "/", credential);
        return server;
    }

    /// <summary>The clone address of a repository.</summary>
    public Uri Url(string repository) => new(Address, repository + ".git");

    /// <summary>Creates an empty bare repository whose default branch is <paramref name="defaultBranch"/>.</summary>
    public async Task CreateAsync(string repository, string defaultBranch = "main") =>
        await GitAsync(root, "init", "--bare", "--quiet", $"--initial-branch={defaultBranch}", Bare(repository)).ConfigureAwait(false);

    /// <summary>
    /// Commits files to a branch (creating it from the default branch or from nothing) and returns the
    /// commit's SHA; a <see langword="null"/> content deletes the file.
    /// </summary>
    public async Task<string> CommitAsync(string repository, IReadOnlyDictionary<string, string?> files, string message, string branch = "main", string author = "Ada Lovelace")
    {
        ArgumentNullException.ThrowIfNull(files);
        var work = Directory.CreateTempSubdirectory("autocanvas-git-work-").FullName;
        try
        {
            await GitAsync(work, "init", "--quiet", $"--initial-branch={branch}", work).ConfigureAwait(false);

            // The branch as it is, or a new one from the default branch, or the first commit.
            var existing = await GitAsync(work, "--git-dir", Bare(repository), "branch", "--list", branch).ConfigureAwait(false);
            var head = await GitAsync(work, "--git-dir", Bare(repository), "branch", "--list").ConfigureAwait(false);
            if (existing.Trim().Length > 0 || head.Trim().Length > 0)
            {
                await GitAsync(work, "-C", work, "fetch", "--quiet", Bare(repository), existing.Trim().Length > 0 ? branch : "HEAD").ConfigureAwait(false);
                await GitAsync(work, "-C", work, "reset", "--quiet", "--hard", "FETCH_HEAD").ConfigureAwait(false);
            }

            foreach (var (path, content) in files)
            {
                var file = Path.Combine(work, path);
                if (content is null)
                {
                    File.Delete(file);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllTextAsync(file, content).ConfigureAwait(false);
                }
            }

            await GitAsync(work, "-C", work, "add", "--all").ConfigureAwait(false);
            await GitAsync(work, "-C", work, "-c", $"user.name={author}", "-c", "user.email=author@example.org", "commit", "--quiet", "--allow-empty", "-m", message).ConfigureAwait(false);
            await GitAsync(work, "-C", work, "push", "--quiet", Bare(repository), $"HEAD:refs/heads/{branch}").ConfigureAwait(false);
            return (await GitAsync(work, "-C", work, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        }
        finally
        {
            DeleteDirectory(work);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync().ConfigureAwait(false);
        DeleteDirectory(root);
    }

    private string Bare(string repository) => Path.Combine(root, repository + ".git");

    /// <summary>Answers one request with <c>git http-backend</c>, run as a CGI program.</summary>
    private async Task ServeAsync(HttpContext context)
    {
        var request = context.Request;
        if (Credential is { } expected
            && !string.Equals(request.Headers.Authorization.ToString(), "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{expected.User}:{expected.Password}")), StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"git\"";
            return;
        }

        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body).ConfigureAwait(false);
        var start = new ProcessStartInfo("git")
        {
            ArgumentList = { "http-backend" },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.Environment["GIT_PROJECT_ROOT"] = root;
        start.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
        start.Environment["PATH_INFO"] = request.Path.Value ?? "/";
        start.Environment["QUERY_STRING"] = request.QueryString.HasValue ? request.QueryString.Value![1..] : string.Empty;
        start.Environment["REQUEST_METHOD"] = request.Method;
        start.Environment["CONTENT_TYPE"] = request.ContentType ?? string.Empty;
        start.Environment["CONTENT_LENGTH"] = body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["REMOTE_ADDR"] = "127.0.0.1";
        start.Environment["REMOTE_USER"] = Credential?.User ?? string.Empty;
        start.Environment["HTTP_CONTENT_ENCODING"] = request.Headers.ContentEncoding.ToString();
        start.Environment["GIT_PROTOCOL"] = request.Headers["Git-Protocol"].ToString();

        using var process = Process.Start(start)!;
        body.Position = 0;
        await body.CopyToAsync(process.StandardInput.BaseStream).ConfigureAwait(false);
        process.StandardInput.Close();
        using var output = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(output).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        // The CGI answer: headers up to an empty line, then the body.
        var bytes = output.ToArray();
        var split = IndexOfBlankLine(bytes, out var separator);
        var headers = Encoding.ASCII.GetString(bytes, 0, Math.Max(split, 0)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var header in headers)
        {
            var colon = header.IndexOf(':', StringComparison.Ordinal);
            var (name, value) = (header[..colon], header[(colon + 1)..].Trim());
            if (string.Equals(name, "Status", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = int.Parse(value[..3], System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                context.Response.Headers[name] = value;
            }
        }

        var content = split < 0 ? 0 : split + separator;
        await context.Response.Body.WriteAsync(bytes.AsMemory(content)).ConfigureAwait(false);
    }

    private static int IndexOfBlankLine(byte[] bytes, out int separator)
    {
        for (var i = 0; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == '\n' && bytes[i + 1] == '\n')
            {
                separator = 2;
                return i;
            }

            if (i < bytes.Length - 3 && bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
            {
                separator = 4;
                return i;
            }
        }

        separator = 0;
        return -1;
    }

    private static async Task<string> GitAsync(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode == 0
            ? await output.ConfigureAwait(false)
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {await error.ConfigureAwait(false)}");
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // Git marks its objects read-only, which Windows refuses to delete.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
