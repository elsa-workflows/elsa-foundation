using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// Reusable-activity publication is the one Elsa operation that commits Design, Runtime and Publishing
/// documents together, and Groundwork has no cross-store transaction. Splitting those lanes must be caught,
/// not silently write runtime documents into the design database.
/// </summary>
public sealed class ActivityPublicationLaneColocationTests
{
    private static readonly IReadOnlyDictionary<string, Type> Lanes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["activities design"] = typeof(ActivitiesDesignGroundworkStorageManifestSource),
        ["runtime"] = typeof(RuntimeGroundworkStorageManifestSource),
        ["publishing"] = typeof(PublishingGroundworkStorageManifestSource)
    };

    [Fact]
    public void Unbound_lanes_all_resolve_to_the_default_target_and_are_colocated()
    {
        var laneTargets = new GroundworkLaneTargets(new GroundworkManifestBindings());

        Assert.True(laneTargets.AreColocated(Lanes));
        Assert.Equal(GroundworkTargetNames.Default, laneTargets.For(typeof(RuntimeGroundworkStorageManifestSource)));
    }

    [Fact]
    public void Lanes_bound_to_one_named_target_are_colocated()
    {
        var bindings = new GroundworkManifestBindings();
        foreach (var lane in Lanes.Values)
            bindings.Bind(lane, "everything");

        Assert.True(new GroundworkLaneTargets(bindings).AreColocated(Lanes));
    }

    [Fact]
    public void A_split_between_design_and_runtime_is_detected()
    {
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(typeof(ActivitiesDesignGroundworkStorageManifestSource), "authoring");
        bindings.Bind(typeof(PublishingGroundworkStorageManifestSource), "authoring");
        var laneTargets = new GroundworkLaneTargets(bindings);

        Assert.False(laneTargets.AreColocated(Lanes));

        var description = laneTargets.Describe(Lanes);
        Assert.Contains("activities design → 'authoring'", description, StringComparison.Ordinal);
        Assert.Contains($"runtime → '{GroundworkTargetNames.Default}'", description, StringComparison.Ordinal);
        Assert.Contains("publishing → 'authoring'", description, StringComparison.Ordinal);
    }
}
