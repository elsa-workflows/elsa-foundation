using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Tests;

/// <summary>
/// End-to-end composition of the provider-neutral target registry against a real provider leaf: the host
/// names its stores, the provider package supplies the leaf, and each target gets its own admitted
/// publication chain.
/// </summary>
public sealed class GroundworkTargetsFeatureTests
{
    private static GroundworkTargetEntry Sqlite(string connectionString) =>
        new() { Provider = SqliteGroundworkDocumentStoreRegistration.ProviderIdentity, ConnectionString = connectionString };

    private static ServiceCollection Compose(
        Action<IServiceCollection> first,
        Action<IServiceCollection> second)
    {
        var services = new ServiceCollection();
        first(services);
        second(services);
        return services;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_named_targets_each_get_their_own_publication_chain(bool providerFirst)
    {
        void AddProvider(IServiceCollection services) =>
            new SqliteGroundworkProviderFeature().ConfigureServices(services);

        void AddTargets(IServiceCollection services) =>
            new GroundworkTargetsFeature
            {
                Targets = new Dictionary<string, GroundworkTargetEntry>
                {
                    ["default"] = Sqlite("Data Source=elsa-runtime.db"),
                    ["authoring"] = Sqlite("Data Source=elsa-design.db")
                }
            }.ConfigureServices(services);

        var services = providerFirst
            ? Compose(AddProvider, AddTargets)
            : Compose(AddTargets, AddProvider);

        var registry = services.GroundworkTargets();
        Assert.Equal(["authoring", "default"], registry.TargetNames);
        Assert.Empty(services.GroundworkTargetComposition().PendingTargets);

        await using var provider = services.BuildServiceProvider();
        var runtimeSource = provider.GetRequiredKeyedService<GroundworkStoreSessionSource>("default");
        var designSource = provider.GetRequiredKeyedService<GroundworkStoreSessionSource>("authoring");
        Assert.NotSame(runtimeSource, designSource);

        // The default target is also the unkeyed publication, so a store that injects IDocumentStore
        // without knowing targets exist resolves the default and nothing else.
        Assert.Same(runtimeSource, provider.GetRequiredService<GroundworkStoreSessionSource>());

        using var scope = provider.CreateScope();
        Assert.NotSame(
            scope.ServiceProvider.GetRequiredKeyedService<IDocumentStore>("default"),
            scope.ServiceProvider.GetRequiredKeyedService<IDocumentStore>("authoring"));
    }

    [Fact]
    public async Task One_admission_is_registered_per_target_behind_a_single_driver()
    {
        var services = Compose(
            s => new SqliteGroundworkProviderFeature().ConfigureServices(s),
            s => new GroundworkTargetsFeature
            {
                Targets = new Dictionary<string, GroundworkTargetEntry>
                {
                    ["default"] = Sqlite("Data Source=a.db"),
                    ["authoring"] = Sqlite("Data Source=b.db")
                }
            }.ConfigureServices(s));

        await using var provider = services.BuildServiceProvider();

        var admissions = provider.GetServices<IGroundworkTargetAdmission>().ToArray();
        Assert.Equal(["authoring", "default"], admissions.Select(a => a.TargetName).Order(StringComparer.Ordinal));
        Assert.Single(provider.GetServices<GroundworkTargetAdmissionInitializer>());
    }

    [Fact]
    public void A_provider_specific_option_is_read_by_the_leaf_that_owns_it()
    {
        var entry = Sqlite("Data Source=a.db");
        entry.Options[SqliteGroundworkProviderLeaf.StoreCacheCapacityOption] = "64";
        entry.Options[SqliteGroundworkProviderLeaf.SkipSchemaInspectionWhenPlanUnchangedOption] = "true";

        var services = Compose(
            s => new SqliteGroundworkProviderFeature().ConfigureServices(s),
            s => new GroundworkTargetsFeature
            {
                Targets = new Dictionary<string, GroundworkTargetEntry> { ["default"] = entry }
            }.ConfigureServices(s));

        Assert.Equal(["default"], services.GroundworkTargets().TargetNames);
    }

    [Fact]
    public async Task A_target_naming_an_uncomposed_provider_fails_readiness_with_the_missing_provider_named()
    {
        var services = new ServiceCollection();
        new SqliteGroundworkProviderFeature().ConfigureServices(services);
        new GroundworkTargetsFeature
        {
            Targets = new Dictionary<string, GroundworkTargetEntry>
            {
                ["default"] = Sqlite("Data Source=a.db"),
                ["authoring"] = new() { Provider = "postgresql", ConnectionString = "Host=pg" }
            }
        }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var readiness = provider.GetRequiredService<GroundworkSchemaReadinessTask>();

        var failure = Assert.Throws<GroundworkSchemaReadinessException>(
            () => readiness.InitializeAsync().GetAwaiter().GetResult());

        Assert.Contains("'authoring'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'postgresql'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'sqlite'", failure.Message, StringComparison.Ordinal);
    }
}
