using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// The point of the whole target mechanism: a host puts the authoring catalog and runtime state in separate
/// databases, and each admits only its own lane's schema. Issue #1156.
/// </summary>
public sealed class SplitLaneTargetCompositionTests
{
    private const string DesignTarget = "authoring";
    private const string RuntimeTarget = "default";

    private static ServiceCollection ComposeSplitHost()
    {
        var services = new ServiceCollection();
        new SqliteGroundworkProviderFeature().ConfigureServices(services);
        new GroundworkTargetsFeature
        {
            Targets = new Dictionary<string, GroundworkTargetEntry>
            {
                [RuntimeTarget] = new()
                {
                    Provider = SqliteGroundworkDocumentStoreRegistration.ProviderIdentity,
                    ConnectionString = "Data Source=elsa-runtime.db"
                },
                [DesignTarget] = new()
                {
                    Provider = SqliteGroundworkDocumentStoreRegistration.ProviderIdentity,
                    ConnectionString = "Data Source=elsa-design.db"
                }
            }
        }.ConfigureServices(services);

        new WorkflowsRuntimeGroundworkPersistenceFeature { Target = RuntimeTarget }.ConfigureServices(services);
        new WorkflowsDesignGroundworkPersistenceFeature { Target = DesignTarget }.ConfigureServices(services);
        new ActivitiesDesignGroundworkPersistenceFeature { Target = DesignTarget }.ConfigureServices(services);
        return services;
    }

    [Fact]
    public async Task Design_and_runtime_lanes_resolve_stores_from_different_targets()
    {
        var services = ComposeSplitHost();

        await using var provider = services.BuildServiceProvider();
        var runtimeSource = provider.GetRequiredKeyedService<GroundworkStoreSessionSource>(RuntimeTarget);
        var designSource = provider.GetRequiredKeyedService<GroundworkStoreSessionSource>(DesignTarget);

        Assert.NotSame(runtimeSource, designSource);
        Assert.Equal([DesignTarget, RuntimeTarget], services.GroundworkTargets().TargetNames);
        Assert.Empty(services.GroundworkTargetComposition().PendingTargets);

        // Each lane's contracts are registered; resolving them proves the registrar bound a store per lane.
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
    }

    [Fact]
    public void Each_target_owns_only_its_own_lanes_manifests()
    {
        var services = ComposeSplitHost();
        var bindings = services.GroundworkManifestBindings();

        Assert.Equal(
            [DesignTarget, RuntimeTarget],
            bindings.BoundTargets.Order(StringComparer.Ordinal));
        Assert.Equal(
            DesignTarget,
            bindings.TargetFor(typeof(Elsa.Workflows.Design.Persistence.Groundwork.WorkflowsDesignGroundworkStorageManifestSource)));
        Assert.Equal(
            RuntimeTarget,
            bindings.TargetFor(typeof(RuntimeGroundworkStorageManifestSource)));
    }

    [Fact]
    public void The_two_design_lanes_cannot_be_pulled_apart_because_they_share_one_ledger()
    {
        var services = new ServiceCollection();
        new SqliteGroundworkProviderFeature().ConfigureServices(services);
        new WorkflowsDesignGroundworkPersistenceFeature { Target = DesignTarget }.ConfigureServices(services);

        var conflict = Assert.Throws<GroundworkTargetConflictException>(() =>
            new ActivitiesDesignGroundworkPersistenceFeature { Target = "elsewhere" }.ConfigureServices(services));

        Assert.Contains("GroundworkDesignAtomicWriteStorageManifestSource", conflict.Message, StringComparison.Ordinal);
        Assert.Contains($"'{DesignTarget}'", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("'elsewhere'", conflict.Message, StringComparison.Ordinal);
    }
}
