namespace RetailCore.Integration.Tests;

/// <summary>
/// Starts each database once for the whole run.
/// </summary>
/// <remarks>
/// The PostgreSQL container takes a few seconds to come up, which is worth paying once but
/// not once per test. Each test still gets its own shop, so they do not interfere.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private SqliteHarness? _sqlite;
    private PostgresHarness? _postgres;

    public static TheoryData<string> ProviderNames { get; } = new("SQLite", "PostgreSQL");

    public async ValueTask InitializeAsync()
    {
        _sqlite = await SqliteHarness.CreateAsync().ConfigureAwait(false);
        _postgres = await PostgresHarness.CreateAsync().ConfigureAwait(false);
    }

    internal IDatabaseHarness For(string provider) => provider switch
    {
        "SQLite" => _sqlite ?? throw new InvalidOperationException("SQLite harness is not ready."),
        "PostgreSQL" => _postgres ?? throw new InvalidOperationException("PostgreSQL harness is not ready."),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider."),
    };

    internal PostgresHarness Postgres =>
        _postgres ?? throw new InvalidOperationException("PostgreSQL harness is not ready.");

    public async ValueTask DisposeAsync()
    {
        if (_sqlite is not null)
        {
            await _sqlite.DisposeAsync().ConfigureAwait(false);
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync().ConfigureAwait(false);
        }
    }
}

[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
