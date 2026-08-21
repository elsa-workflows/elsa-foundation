using Elsa.Persistence.Groundwork.Composition;
using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

/// <summary>
/// Issue #1211. <see cref="WorkflowsDashboardGroundworkPersistenceFeature"/> is the relational counterpart of
/// the MongoDB dashboard wiring, and had no tests: every decision it makes — which lane's target to consult,
/// whether the lanes are split, which SQL dialect to render, what to do when a provider offers no connection
/// — was unasserted.
/// <para>
/// That is the gap that produced #1173 and the <c>GroundworkLaneStores</c> startup break: registration
/// mistakes are invisible to unit suites that never resolve the service from a container. These resolve it,
/// and need no database, because nothing here opens a connection — the feature only hands the data sources a
/// factory.
/// </para>
/// </summary>
public sealed class WorkflowsDashboardGroundworkLaneWiringTests
{
    [Fact]
    public void Co_located_lanes_serve_the_portfolio_from_the_one_target()
    {
        var services = Services();
        Source(services, GroundworkTargetNames.Default, "sqlite");
        BindDesignLaneTo(services, null);
        BindRuntimeLaneTo(services, null);
        new WorkflowsDashboardGroundworkPersistenceFeature().ConfigureServices(services);

        using var scope = Scope(services);

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
    }

    [Fact]
    public void Split_lanes_serve_the_portfolio_from_both_targets()
    {
        var services = Services();
        Source(services, GroundworkTargetNames.Default, "sqlite");
        Source(services, "authoring", "sqlite");
        BindDesignLaneTo(services, "authoring");
        BindRuntimeLaneTo(services, null);
        new WorkflowsDashboardGroundworkPersistenceFeature().ConfigureServices(services);

        using var scope = Scope(services);

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
    }

    /// <summary>
    /// The assertion that matters most. On a split host the portfolio counts design-lane definitions against
    /// a published count that only the runtime lane knows, so it must consult BOTH targets. #1173 was exactly
    /// a tile that never consulted the runtime lane and reported zero. Leaving the runtime target without a
    /// connection source makes the lookup observable without a database: refusing proves it asked.
    /// </summary>
    [Fact]
    public void A_split_portfolio_refuses_when_the_runtime_lane_has_no_connection_source()
    {
        var services = Services();
        Source(services, "authoring", "sqlite");
        BindDesignLaneTo(services, "authoring");
        BindRuntimeLaneTo(services, "runtime-elsewhere");
        new WorkflowsDashboardGroundworkPersistenceFeature().ConfigureServices(services);

        using var scope = Scope(services);

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
        Assert.Contains("runtime lane", exception.Message, StringComparison.Ordinal);
        Assert.Contains("runtime-elsewhere", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_portfolio_refuses_when_the_design_lane_has_no_connection_source()
    {
        var services = Services();
        Source(services, GroundworkTargetNames.Default, "sqlite");
        BindDesignLaneTo(services, "authoring");
        BindRuntimeLaneTo(services, null);
        new WorkflowsDashboardGroundworkPersistenceFeature().ConfigureServices(services);

        using var scope = Scope(services);

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
        Assert.Contains("design lane", exception.Message, StringComparison.Ordinal);
        Assert.Contains("authoring", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_health_follows_the_runtime_lane_and_refuses_a_target_without_a_connection_source()
    {
        var services = Services();
        Source(services, "authoring", "sqlite");
        BindDesignLaneTo(services, "authoring");
        BindRuntimeLaneTo(services, "runtime-elsewhere");
        new WorkflowsDashboardGroundworkPersistenceFeature().ConfigureServices(services);

        using var scope = Scope(services);

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IWorkflowRunHealthDataSource>());
        Assert.Contains("run health", exception.Message, StringComparison.Ordinal);
        Assert.Contains("runtime-elsewhere", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    public void Every_relational_provider_leaf_has_a_dashboard_dialect(string providerIdentity)
    {
        var services = Services();
        Source(services, GroundworkTargetNames.Default, providerIdentity);
        BindDesignLaneTo(services, null);
        BindRuntimeLaneTo(services, null);
        new WorkflowsDashboardGroundworkPersistenceFeature().ConfigureServices(services);

        using var scope = Scope(services);

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowRunHealthDataSource>());
    }

    /// <summary>
    /// A provider with no dialect must say so. MongoDB reaches this path if a host binds a lane to a document
    /// target and still enables the relational dashboard feature: it supplies no connection source at all,
    /// so the refusal comes first — but a future relational leaf without a dialect entry would land here.
    /// </summary>
    [Fact]
    public void A_provider_without_a_dashboard_dialect_is_refused()
    {
        var services = Services();
        Source(services, GroundworkTargetNames.Default, "some-future-provider");
        BindDesignLaneTo(services, null);
        BindRuntimeLaneTo(services, null);
        new WorkflowsDashboardGroundworkPersistenceFeature().ConfigureServices(services);

        using var scope = Scope(services);

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IWorkflowRunHealthDataSource>());
        Assert.Contains("some-future-provider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dialect", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, StubPayloadSerializer>();
        return services;
    }

    private static IServiceScope Scope(IServiceCollection services) =>
        services.BuildServiceProvider().CreateScope();

    private static void Source(IServiceCollection services, string target, string providerIdentity) =>
        services.AddKeyedSingleton<IGroundworkTargetConnectionSource>(
            target,
            (_, key) => new StubConnectionSource((string)key, providerIdentity));

    private static void BindDesignLaneTo(IServiceCollection services, string? target) =>
        services.AddGroundworkStorageLane<WorkflowsDesignGroundworkStorageManifestSource>(target);

    private static void BindRuntimeLaneTo(IServiceCollection services, string? target) =>
        services.AddGroundworkManifestSource<RuntimeGroundworkStorageManifestSource>(target);

    /// <summary>Never opened: the feature only hands the data sources a factory.</summary>
    private sealed class StubConnectionSource(string targetName, string providerIdentity)
        : IGroundworkTargetConnectionSource
    {
        public string TargetName { get; } = targetName;

        public string ProviderIdentity { get; } = providerIdentity;

        public DbConnection CreateConnection() => new SqliteConnection("Data Source=:memory:");
    }

    private sealed class StubPayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);

        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);

        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;

        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;

        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;

        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;

        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;

        public JsonSerializerOptions GetOptions() => Options;
    }
}
