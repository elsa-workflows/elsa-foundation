using Elsa.Workflows.Design.Core.Models;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-006 + Unit C FR-011: after the collapse, zero occurrences of the old
/// <c>(activityDefinitionId : string, version : int)</c> pair remain on <c>ActivityNode</c>.
/// A single <see cref="ActivityNode.ActivityVersionId"/> string replaces the pair —
/// the stable catalog reference owned by Unit B (FR-011a — string typing is the seam;
/// no shared contract type introduced).
/// </summary>
public sealed class ActivityVersionIdCollapseTests
{
    [Fact]
    public void ActivityNode_has_no_ActivityDefinitionId_property()
    {
        var members = typeof(ActivityNode)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.DoesNotContain("ActivityDefinitionId", members);
    }

    [Fact]
    public void ActivityNode_has_no_ActivityVersion_property()
    {
        var members = typeof(ActivityNode)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.DoesNotContain("ActivityVersion", members);
    }

    [Fact]
    public void ActivityNode_has_ActivityVersionId_string()
    {
        var property = typeof(ActivityNode)
            .GetProperty(nameof(ActivityNode.ActivityVersionId), BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);
    }

    [Fact]
    public void ActivityNode_exposes_activity_owned_structure()
    {
        var property = typeof(ActivityNode)
            .GetProperty(nameof(ActivityNode.Structure), BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.Equal(typeof(ActivityNodeStructure), property!.PropertyType);
    }

    [Fact]
    public void ActivityNode_has_no_persisted_child_slots()
    {
        var property = typeof(ActivityNode)
            .GetProperty("ChildSlots", BindingFlags.Instance | BindingFlags.Public);

        Assert.Null(property);
    }

    [Fact]
    public void ActivityNode_has_no_generic_Composition_property()
    {
        var members = typeof(ActivityNode)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.DoesNotContain("Composition", members);
    }

    [Fact]
    public void WorkflowsDesignCore_defines_no_activity_specific_child_slot_name_or_metadata_catalog()
    {
        var assembly = typeof(ActivityNode).Assembly;

        Assert.Null(assembly.GetType("Elsa.Workflows.Design.Core.Models.ActivityChildSlotNames"));
        Assert.Null(assembly.GetType("Elsa.Workflows.Design.Core.Models.ActivityChildSlotMetadataKeys"));
    }

    [Fact]
    public void ActivityChildProjection_is_projection_only()
    {
        var members = typeof(ActivityChildProjection)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.Contains(nameof(ActivityChildProjection.Name), members);
        Assert.Contains(nameof(ActivityChildProjection.Activities), members);
        Assert.DoesNotContain("Metadata", members);
    }
}
