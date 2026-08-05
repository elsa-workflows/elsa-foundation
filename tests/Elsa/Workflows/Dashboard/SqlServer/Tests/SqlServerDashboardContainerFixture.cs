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

    /// <summary>
    /// Skips the calling test when Docker is unavailable, then drops and recreates the shared container's
    /// database so the test starts against an empty one.
    /// </summary>
    /// <remarks>
    /// The whole collection shares one container <i>and</i> one database, and SQL Server materialization
    /// admits a manifest against that database as a whole: <c>BackfillOrValidateIdentityRowsAsync</c> scans
    /// every row in <c>groundwork_documents</c> regardless of storage scope and rejects any storage unit the
    /// incoming manifest does not declare. So a class that admits the design manifest and writes
    /// <c>workflowDefinition</c> rows would make the next class's runtime-manifest admission fail, even
    /// though the two use different tenants. Resetting per test keeps each admission honest.
    /// Safe because xUnit runs the classes of a single collection sequentially. Never reset a driver that
    /// another test could still be using.
    /// </remarks>
    public async Task<SqlServerGroundworkProviderDriver> RequireCleanDriverAsync()
    {
        Skip.IfNot(IsAvailable, SkipReason ?? "Docker unavailable.");
        await Driver.ResetAsync();
        return Driver;
    }

    public async Task DisposeAsync() => await Driver.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerDashboardContainerCollection : ICollectionFixture<SqlServerDashboardContainerFixture>
{
    public const string Name = "sqlserver-dashboard-container";
}
