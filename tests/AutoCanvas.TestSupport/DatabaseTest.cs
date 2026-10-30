using AutoCanvas.Persistence;
using Xunit;

namespace AutoCanvas.TestSupport;

/// <summary>
/// Base for tests that need a database: each test gets a fresh, empty one on the given provider.
/// Derive an abstract class with the tests and one sealed class per provider, so every test runs
/// on both and a failure names the provider.
/// </summary>
/// <param name="provider">The provider this test class runs on.</param>
public abstract class DatabaseTest(DatabaseProvider provider) : IAsyncLifetime
{
    private TestDatabase? database;

    /// <summary>The provider this test class runs on.</summary>
    protected DatabaseProvider Provider { get; } = provider;

    /// <summary>The empty database of the current test.</summary>
    protected TestDatabase Database => database ?? throw new InvalidOperationException("The database is created before each test.");

    /// <summary>Cancellation of the current test.</summary>
    protected static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => database = await TestDatabase.CreateAsync(Provider).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
