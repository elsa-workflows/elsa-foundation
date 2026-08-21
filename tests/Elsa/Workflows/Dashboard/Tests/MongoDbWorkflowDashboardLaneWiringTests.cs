using Elsa.Persistence.Groundwork.Composition;
using System.Text.Json;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;
using Elsa.Persistence.Groundwork.MongoDb.Targets;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Serialization.Core;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.MongoDb;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

/// <summary>
/// Issue #1173: the MongoDB portfolio tile counts design-lane definitions alongside how many are published,
/// which is a runtime-lane fact. It used to be handed one database at registration time by whichever preset
/// composed it, so a host that bound the design lane to a second target kept reading the first: the
/// co-located <c>$lookup</c> then found no source-reference collection and the published count came back
/// <b>zero</b> rather than failing.
/// <para>
/// These assert the wiring, which is where the defect lived — the data source already supported the split.
/// They need no MongoDB server: nothing here opens a database, because the failure was in which database the
/// tile was pointed at, not in the query. That also keeps them inside the PR-time fast gate, which the
/// Testcontainers dashboard project is excluded from.
/// </para>
/// </summary>
public sealed class MongoDbWorkflowDashboardLaneWiringTests
{
    private const string Connection = "mongodb://localhost:27017";

    [Fact]
    public void The_mongo_leaf_publishes_a_database_source_for_every_target_it_declares()
    {
        var services = Services();
        services.AddMongoDbGroundworkDocumentStore(Connection, "elsa-runtime");
        services.AddMongoDbGroundworkDocumentStore(Connection, "elsa-design", targetName: "authoring");

        using var provider = services.BuildServiceProvider();

        var @default = provider.GetRequiredKeyedService<IGroundworkTargetMongoDatabaseSource>(GroundworkTargetNames.Default);
        var authoring = provider.GetRequiredKeyedService<IGroundworkTargetMongoDatabaseSource>("authoring");
        Assert.Equal(GroundworkTargetNames.Default, @default.TargetName);
        Assert.Equal("elsa-runtime", @default.DatabaseName);
        Assert.Equal("authoring", authoring.TargetName);
        Assert.Equal("elsa-design", authoring.DatabaseName);
    }

    [Fact]
    public void Co_located_lanes_serve_the_portfolio_from_the_one_target()
    {
        var services = Services();
        services.AddMongoDbGroundworkDocumentStore(Connection, "elsa");
        BindDesignLaneTo(services, null);
        BindRuntimeLaneTo(services, null);
        services.AddGroundworkMongoDbWorkflowDashboardForLanes();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
    }

    [Fact]
    public void Split_lanes_serve_the_portfolio_from_both_targets()
    {
        var services = Services();
        services.AddMongoDbGroundworkDocumentStore(Connection, "elsa-runtime");
        services.AddMongoDbGroundworkDocumentStore(Connection, "elsa-design", targetName: "authoring");
        BindDesignLaneTo(services, "authoring");
        BindRuntimeLaneTo(services, null);
        services.AddGroundworkMongoDbWorkflowDashboardForLanes();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
    }

    /// <summary>
    /// The assertion that actually pins #1173. With the lanes split, the tile must consult the RUNTIME
    /// lane's target; the old wiring never looked at it at all, so it composed happily against the design
    /// database alone and reported zero. Leaving the runtime target without a MongoDB source makes that
    /// lookup observable without a server: refusing proves it asked.
    /// </summary>
    [Fact]
    public void A_split_portfolio_refuses_when_the_runtime_lane_has_no_mongo_database()
    {
        var services = Services();
        services.AddMongoDbGroundworkDocumentStore(Connection, "elsa-design", targetName: "authoring");
        BindDesignLaneTo(services, "authoring");
        BindRuntimeLaneTo(services, "runtime-elsewhere");
        services.AddGroundworkMongoDbWorkflowDashboardForLanes();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
        Assert.Contains("runtime lane", exception.Message, StringComparison.Ordinal);
        Assert.Contains("runtime-elsewhere", exception.Message, StringComparison.Ordinal);
        // The diagnostic names the target, never the connection string.
        Assert.DoesNotContain(Connection, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_health_follows_the_runtime_lane_and_refuses_a_target_without_a_mongo_database()
    {
        var services = Services();
        services.AddMongoDbGroundworkDocumentStore(Connection, "elsa-design", targetName: "authoring");
        BindDesignLaneTo(services, "authoring");
        BindRuntimeLaneTo(services, "runtime-elsewhere");
        services.AddGroundworkMongoDbWorkflowDashboardForLanes();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IWorkflowRunHealthDataSource>());
        Assert.Contains("run health", exception.Message, StringComparison.Ordinal);
        Assert.Contains("runtime-elsewhere", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The source owns its <see cref="MongoDB.Driver.MongoClient"/>, so it disposes it — but constructing a
    /// client starts the driver's cluster monitoring, so a target whose tile is never rendered must not build
    /// one just to release it. An unparseable connection string makes both halves observable without a
    /// server: constructing and disposing stay silent, and only reaching for the database throws.
    /// </summary>
    [Fact]
    public void The_database_source_builds_its_client_lazily_and_disposes_only_what_it_built()
    {
        var source = new GroundworkTargetMongoDatabaseSource(
            GroundworkTargetNames.Default,
            "this-is-not-a-mongodb-connection-string",
            "elsa");

        source.Dispose();

        var reused = new GroundworkTargetMongoDatabaseSource(
            GroundworkTargetNames.Default,
            "this-is-not-a-mongodb-connection-string",
            "elsa");
        Assert.ThrowsAny<Exception>(() => reused.GetDatabase());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadSerializer, StubPayloadSerializer>();
        return services;
    }

    private static void BindDesignLaneTo(IServiceCollection services, string? target) =>
        services.AddGroundworkStorageLane<WorkflowsDesignGroundworkStorageManifestSource>(target);

    private static void BindRuntimeLaneTo(IServiceCollection services, string? target) =>
        services.AddGroundworkManifestSource<RuntimeGroundworkStorageManifestSource>(target);

    /// <summary>Present only so the portfolio data source can be constructed; nothing here serializes.</summary>
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
