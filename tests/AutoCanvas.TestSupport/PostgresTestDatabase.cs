using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Postgres;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace AutoCanvas.TestSupport;

/// <summary>
/// A database of its own on a PostgreSQL server shared by the whole test run: the server from
/// <c>AUTOCANVAS_TEST_POSTGRES</c> if set, otherwise a container started with Testcontainers.
/// Without either, tests are skipped — unless <c>AUTOCANVAS_REQUIRE_POSTGRES=1</c> (CI), where
/// they fail.
/// </summary>
public sealed class PostgresTestDatabase : TestDatabase
{
    /// <summary>Connection string of a PostgreSQL server to use instead of a container.</summary>
    public const string ServerVariable = "AUTOCANVAS_TEST_POSTGRES";

    /// <summary>Set to <c>1</c> to fail instead of skip when PostgreSQL is unavailable.</summary>
    public const string RequireVariable = "AUTOCANVAS_REQUIRE_POSTGRES";

    private const string Image = "postgres:17-alpine";
    private static readonly SemaphoreSlim ServerLock = new(1, 1);
    private static string? server;
    private static PostgreSqlContainer? container;

    private readonly string name;

    private PostgresTestDatabase(string name, string connectionString)
    {
        this.name = name;
        ConnectionString = connectionString;
    }

    /// <inheritdoc />
    public override DatabaseProvider Provider => DatabaseProvider.Postgres;

    /// <inheritdoc />
    public override string ConnectionString { get; }

    /// <summary>Creates an empty database with a unique name.</summary>
    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var serverConnectionString = await GetServerAsync().ConfigureAwait(false);
        var databaseName = $"autocanvas_test_{Guid.NewGuid():N}";
        await ExecuteAsync(serverConnectionString, $"CREATE DATABASE \"{databaseName}\"").ConfigureAwait(false);
        var connectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName }.ConnectionString;
        return new PostgresTestDatabase(databaseName, connectionString);
    }

    /// <inheritdoc />
    public override AutoCanvasDbContext CreateContext(ILoggerFactory? loggerFactory = null)
    {
        var options = new DbContextOptionsBuilder<PostgresAutoCanvasDbContext>();
        options.UseAutoCanvasPostgres(ConnectionString);
        if (loggerFactory is not null)
        {
            options.UseLoggerFactory(loggerFactory);
        }

        return new PostgresAutoCanvasDbContext(options.Options);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(server!, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)").ConfigureAwait(false);
    }

    private static async Task<string> GetServerAsync()
    {
        await ServerLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (server is not null)
            {
                return server;
            }

            var configured = Environment.GetEnvironmentVariable(ServerVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return server = configured;
            }

            try
            {
                // Testcontainers' reaper removes the container when the test run ends. The data
                // lives in memory: faster, and no anonymous Docker volume is left behind per run.
                container = new PostgreSqlBuilder(Image).WithTmpfsMount("/var/lib/postgresql/data").Build();
                await container.StartAsync().ConfigureAwait(false);
                return server = container.GetConnectionString();
            }
            catch (DockerUnavailableException exception)
            {
                const string reason = $"PostgreSQL is not available: Docker is not running and {ServerVariable} is not set.";
                if (string.Equals(Environment.GetEnvironmentVariable(RequireVariable), "1", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(reason, exception);
                }

                Assert.Skip(reason);
                throw;
            }
        }
        finally
        {
            ServerLock.Release();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Only CREATE/DROP DATABASE with names generated from a GUID; no input reaches the SQL.")]
    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }
}
