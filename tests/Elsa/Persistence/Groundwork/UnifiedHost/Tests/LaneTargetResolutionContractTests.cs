using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// Lane targets are keyed by manifest <em>source</em> type, and an unbound source legitimately resolves to
/// the default target. That makes passing the wrong type indistinguishable from an unbound lane, which is
/// exactly how a cross-lane write once ended up addressing the default store instead of the lane's own.
/// These pin the boundary so the mistake cannot recur silently.
/// </summary>
public sealed class LaneTargetResolutionContractTests
{
    private static GroundworkLaneTargets Bound(string target)
    {
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(typeof(PublishingGroundworkStorageManifestSource), target);
        return new GroundworkLaneTargets(bindings);
    }

    [Fact]
    public void A_manifest_type_is_rejected_rather_than_silently_resolving_to_the_default_target()
    {
        var laneTargets = Bound("authoring");

        var failure = Assert.Throws<ArgumentException>(
            () => laneTargets.For(typeof(PublishingGroundworkStorageManifest)));

        Assert.Contains(nameof(IGroundworkStorageManifestSource), failure.Message, StringComparison.Ordinal);
        Assert.Contains("a manifest type resolves nothing", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manifest_source_type_resolves_the_lane_it_was_bound_to()
    {
        Assert.Equal("authoring", Bound("authoring").For<PublishingGroundworkStorageManifestSource>());
    }

    [Fact]
    public void An_unbound_lane_still_resolves_to_the_default_target()
    {
        Assert.Equal(
            GroundworkTargetNames.Default,
            Bound("authoring").For<ActivitiesDesignGroundworkStorageManifestSource>());
    }

    [Fact]
    public void The_store_resolver_rejects_a_manifest_type_too()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentStore>(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized()));
        using var provider = services.BuildServiceProvider();
        var laneStores = new GroundworkLaneStores(provider, Bound("authoring"));

        Assert.Throws<ArgumentException>(() => laneStores.For(typeof(PublishingGroundworkStorageManifest)));
    }

    [Fact]
    public void The_store_resolver_reaches_the_bound_target_not_the_default_one()
    {
        var designStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var defaultStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IDocumentStore>("authoring", designStore);
        services.AddKeyedSingleton<IDocumentStore>(GroundworkTargetNames.Default, defaultStore);
        using var provider = services.BuildServiceProvider();

        var laneStores = new GroundworkLaneStores(provider, Bound("authoring"));

        Assert.Same(designStore, laneStores.For<PublishingGroundworkStorageManifestSource>());
        Assert.Same(defaultStore, laneStores.For<RuntimeGroundworkStorageManifestSource>());
    }
}
