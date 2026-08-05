using DotNet.Testcontainers.Builders;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Workflows.Dashboard.SqlServer.Tests;

/// <summary>
/// Starts a single throwaway SQL Server container (via <see cref="SqlServerGroundworkProviderDriver"/>) for
/// the whole test collection. If Docker is unavailable the driver fails to initialize; the fixture then
/// flips <see cref="IsAvailable"/> to <c>false</c> so integration tests can skip gracefully instead of
/// failing with an opaque connection error.
/// </summary>
public sealed class SqlServerDashboardContainerFixture : IAsyncLifetime
{
    /// <summary>The real SQL Server provider driver backing this fixture's single container.</summary>
    public SqlServerGroundworkProviderDriver Driver { get; } = new();

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
            SkipReason = $"Docker/SQL Server container unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync() => await Driver.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerDashboardContainerCollection : ICollectionFixture<SqlServerDashboardContainerFixture>
{
    public const string Name = "sqlserver-dashboard-container";
}
