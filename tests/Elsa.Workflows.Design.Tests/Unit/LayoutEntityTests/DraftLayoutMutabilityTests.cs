using Elsa.Workflows.Design.Persistence.Core.Entities;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.LayoutEntityTests;

/// <summary>
/// SC-021 + Unit C FR-006a: <c>WorkflowDefinitionDraftLayout</c> mirrors the Draft's
/// mutability — no write-once restrictions; standard mutable tracking. The R5 cascade
/// behaviour (OnDelete: Cascade) is configured in
/// <c>WorkflowDefinitionDraftLayoutConfiguration</c>.
/// </summary>
public sealed class DraftLayoutMutabilityTests
{
    [Fact]
    public void Entity_specific_properties_have_mutable_setters()
    {
        // Draft-specific properties (FK + Records) must NOT be init-only
        // because the Draft layout mutates as the author edits the canvas.
        AssertMutableSetter(nameof(WorkflowDefinitionDraftLayout.WorkflowDefinitionDraftId));
        AssertMutableSetter(nameof(WorkflowDefinitionDraftLayout.Records));
    }

    [Fact]
    public void Records_property_has_setter()
    {
        var setter = typeof(WorkflowDefinitionDraftLayout)
            .GetProperty(nameof(WorkflowDefinitionDraftLayout.Records))!
            .SetMethod;

        Assert.NotNull(setter);
        // not init-only — Draft layout is mutable across the entity's lifetime
        var hasInitModifier = setter!.ReturnParameter.GetRequiredCustomModifiers()
            .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        Assert.False(hasInitModifier, "Records setter must be a regular mutable setter");
    }

    [Fact]
    public void Entity_is_sealed()
    {
        Assert.True(typeof(WorkflowDefinitionDraftLayout).IsSealed);
    }

    private static void AssertMutableSetter(string propertyName)
    {
        var setter = typeof(WorkflowDefinitionDraftLayout).GetProperty(propertyName)!.SetMethod!;
        var hasInitModifier = setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        Assert.False(hasInitModifier, $"{propertyName} must not be init-only");
    }
}
