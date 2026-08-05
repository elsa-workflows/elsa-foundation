using DotNet.Testcontainers.Builders;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Workflows.Dashboard.MongoDb.Tests;

/// <summary>
/// Starts a single throwaway MongoDB replica-set container (via <see cref="MongoDbGroundworkProviderDriver"/>)
/// for the whole test collection. If Docker is unavailable the driver fails to initialize; the fixture then
/// flips <see cref="IsAvailable"/> to <c>false</c> so integration tests can skip gracefully instead of failing
/// with an opaque connection error.
/// </summary>
public sealed class MongoDbDashboardContainerFixture : IAsyncLifetime
{
    /// <summary>The real MongoDB provider driver backing this fixture's single container.</summary>
    public MongoDbGroundworkProviderDriver Driver { get; } = new();

    /// <summary>True when the container started and the driver initialized against it.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Reason the container is unavailable, for diagnostics in skipped tests.</summary>
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await Driver.InitializeAsync();
            IsAvailable = true;
        }
        catch (DockerUnavailableException exception)
        {
            IsAvailable = false;
            SkipReason = $"Docker/MongoDB container unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync() => await Driver.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class MongoDbDashboardContainerCollection : ICollectionFixture<MongoDbDashboardContainerFixture>
{
    public const string Name = "mongodb-dashboard-container";
}
