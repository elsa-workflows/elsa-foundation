using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// Reusable-activity publication is the one operation writing Design, Runtime and Publishing documents
/// together. Co-located that is one transaction; split it becomes an ordered sequence, and these pin the
/// two properties that make the ordering safe: each lane is addressed through its own target's store, and
/// the mode is chosen from the actual bindings rather than assumed. Issue #1156, ADR 0066.
/// </summary>
public sealed class SplitActivityPublicationOrderingTests
{
    private static readonly Type DesignLane = typeof(ActivitiesDesignGroundworkStorageManifestSource);
    private static readonly Type RuntimeLane = typeof(RuntimeGroundworkStorageManifestSource);
    private static readonly Type PublishingLane = typeof(PublishingGroundworkStorageManifestSource);

    [Fact]
    public void Unbound_lanes_take_the_single_transaction_path()
    {
        var laneTargets = new GroundworkLaneTargets(new GroundworkManifestBindings());

        Assert.True(ActivityPublicationLaneColocation.AreColocated(laneTargets));
    }

    [Fact]
    public void Lanes_on_one_named_target_take_the_single_transaction_path()
    {
        var bindings = new GroundworkManifestBindings();
        foreach (var lane in new[] { DesignLane, RuntimeLane, PublishingLane })
            bindings.Bind(lane, "everything");

        Assert.True(ActivityPublicationLaneColocation.AreColocated(new GroundworkLaneTargets(bindings)));
    }

    [Fact]
    public void A_split_takes_the_ordered_path_and_reports_the_mapping()
    {
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(DesignLane, "authoring");
        bindings.Bind(PublishingLane, "authoring");
        var laneTargets = new GroundworkLaneTargets(bindings);

        Assert.False(ActivityPublicationLaneColocation.AreColocated(laneTargets));

        var description = ActivityPublicationLaneColocation.Describe(laneTargets);
        Assert.Contains("activities design → 'authoring'", description, StringComparison.Ordinal);
        Assert.Contains($"runtime → '{GroundworkTargetNames.Default}'", description, StringComparison.Ordinal);
    }

    private static IDocumentStore NewStore() =>
        new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());

    [Fact]
    public void Each_lane_resolves_the_store_of_the_target_it_is_bound_to()
    {
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(DesignLane, "authoring");
        bindings.Bind(PublishingLane, "authoring");

        var designStore = NewStore();
        var runtimeStore = NewStore();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IDocumentStore>("authoring", designStore);
        services.AddKeyedSingleton<IDocumentStore>(GroundworkTargetNames.Default, runtimeStore);
        using var provider = services.BuildServiceProvider();

        var laneStores = new GroundworkLaneStores(provider, new GroundworkLaneTargets(bindings));

        Assert.Same(designStore, laneStores.For(DesignLane));
        Assert.Same(designStore, laneStores.For(PublishingLane));
        Assert.Same(runtimeStore, laneStores.For(RuntimeLane));
    }

    [Fact]
    public void A_lane_on_an_undeclared_named_target_refuses_rather_than_using_the_default_store()
    {
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(RuntimeLane, "runtime");

        var services = new ServiceCollection();
        services.AddSingleton<IDocumentStore>(NewStore());
        using var provider = services.BuildServiceProvider();
        var laneStores = new GroundworkLaneStores(provider, new GroundworkLaneTargets(bindings));

        var failure = Assert.Throws<InvalidOperationException>(() => laneStores.For(RuntimeLane));

        Assert.Contains("'runtime'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Declare the target", failure.Message, StringComparison.Ordinal);
    }

}
