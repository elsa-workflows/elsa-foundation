using Elsa.Persistence.Groundwork.Targets;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Tests;

/// <summary>
/// Binding a lane's manifest source to a target is what lets one host admit several physical stores that
/// each carry only their own storage units. These assert the binding rules a composition depends on.
/// </summary>
public sealed class GroundworkManifestBindingTests
{
    private sealed class LaneA;

    private sealed class LaneB;

    [Fact]
    public void An_unbound_source_belongs_to_the_default_target()
    {
        var bindings = new GroundworkManifestBindings();

        Assert.Equal(GroundworkTargetNames.Default, bindings.TargetFor(typeof(LaneA)));
        Assert.Empty(bindings.BoundTargets);
    }

    [Fact]
    public void Binding_the_same_source_to_the_same_target_twice_is_idempotent()
    {
        var bindings = new GroundworkManifestBindings();

        bindings.Bind(typeof(LaneA), "authoring");
        bindings.Bind(typeof(LaneA), "authoring");

        Assert.Equal("authoring", bindings.TargetFor(typeof(LaneA)));
        Assert.Equal(["authoring"], bindings.BoundTargets);
    }

    [Fact]
    public void A_source_cannot_be_bound_to_two_named_targets()
    {
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(typeof(LaneA), "authoring");

        var conflict = Assert.Throws<GroundworkTargetConflictException>(
            () => bindings.Bind(typeof(LaneA), "runtime"));

        Assert.Contains("'authoring'", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("'runtime'", conflict.Message, StringComparison.Ordinal);
        Assert.Contains("belong", conflict.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An aggregate preset registers a lane without naming a target, so an unspecified binding must not
    /// fight the host's explicit placement. This has to hold in either composition order, because a preset
    /// may run before or after the feature that names the target.
    /// </summary>
    [Fact]
    public void An_unspecified_binding_yields_to_an_explicit_one_in_either_order()
    {
        var explicitFirst = new GroundworkManifestBindings();
        explicitFirst.Bind(typeof(LaneA), "authoring");
        explicitFirst.Bind(typeof(LaneA), null);

        var unspecifiedFirst = new GroundworkManifestBindings();
        unspecifiedFirst.Bind(typeof(LaneA), null);
        unspecifiedFirst.Bind(typeof(LaneA), "authoring");

        Assert.Equal("authoring", explicitFirst.TargetFor(typeof(LaneA)));
        Assert.Equal("authoring", unspecifiedFirst.TargetFor(typeof(LaneA)));
    }

    [Fact]
    public void An_explicit_default_still_conflicts_with_another_named_target()
    {
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(typeof(LaneA), GroundworkTargetNames.Default);

        Assert.Throws<GroundworkTargetConflictException>(
            () => bindings.Bind(typeof(LaneA), "authoring"));
    }

    [Fact]
    public void Distinct_sources_may_occupy_distinct_targets()
    {
        var bindings = new GroundworkManifestBindings();

        bindings.Bind(typeof(LaneA), "authoring");
        bindings.Bind(typeof(LaneB), "runtime");

        Assert.Equal(["authoring", "runtime"], bindings.BoundTargets);
    }

    [Fact]
    public void An_absent_or_blank_target_name_means_the_default_target()
    {
        var bindings = new GroundworkManifestBindings();

        bindings.Bind(typeof(LaneA), null);
        bindings.Bind(typeof(LaneB), "   ");

        Assert.Equal(GroundworkTargetNames.Default, bindings.TargetFor(typeof(LaneA)));
        Assert.Equal(GroundworkTargetNames.Default, bindings.TargetFor(typeof(LaneB)));
    }

    [Theory]
    [InlineData(null, GroundworkTargetNames.Default)]
    [InlineData("", GroundworkTargetNames.Default)]
    [InlineData("  ", GroundworkTargetNames.Default)]
    [InlineData(" authoring ", "authoring")]
    [InlineData("Authoring", "Authoring")]
    public void Target_names_are_trimmed_but_compared_ordinally(string? configured, string expected)
    {
        Assert.Equal(expected, GroundworkTargetNames.Normalize(configured));
    }

    [Fact]
    public void Target_names_are_case_sensitive_so_two_casings_are_two_stores()
    {
        var bindings = new GroundworkManifestBindings();

        bindings.Bind(typeof(LaneA), "authoring");
        bindings.Bind(typeof(LaneB), "Authoring");

        Assert.Equal(["Authoring", "authoring"], bindings.BoundTargets);
    }
}
