using DotNet.Testcontainers.Builders;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.PostgreSql.Tests;

/// <summary>
/// Starts a single throwaway PostgreSQL container for the whole test collection and exposes its connection
/// string. If Docker is unavailable (CI without a daemon, sandbox, …) the container fails to start; the
/// fixture then flips <see cref="IsAvailable"/> to <c>false</c> so integration tests can skip gracefully
/// instead of failing. On this workstation Docker is present, so the container starts and the tests run.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlGroundworkProviderDriver _driver = new();

    /// <summary>True when the container started and a live PostgreSQL is reachable.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Reason the container is unavailable, for diagnostics in skipped tests.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>
    /// Creates a fresh, uniquely-named database on the running container and returns a connection string to it,
    /// so each integration test materializes into an isolated schema and cannot collide with its neighbours.
    /// </summary>
    public Task<string> CreateIsolatedDatabaseAsync() => _driver.CreateIsolatedDatabaseAsync();

    public async Task InitializeAsync()
    {
        try
        {
            await _driver.InitializeAsync();
            IsAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            IsAvailable = false;
            SkipReason = "Docker/PostgreSQL container unavailable.";
        }
    }

    public async Task DisposeAsync() => await _driver.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresContainerCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "postgres-container";
}
