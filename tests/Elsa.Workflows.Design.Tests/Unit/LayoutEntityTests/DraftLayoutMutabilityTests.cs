using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.LayoutEntityTests;

/// <summary>
/// SC-021 + Unit C FR-006a: <c>WorkflowDefinitionDraftLayout</c> mirrors the Draft's
/// mutability — no <c>[Immutable]</c> markers, standard mutable tracking. The R5 cascade
/// behaviour (OnDelete: Cascade) is configured in
/// <c>WorkflowDefinitionDraftLayoutConfiguration</c>.
/// </summary>
public sealed class DraftLayoutMutabilityTests
{
    [Fact]
    public void Entity_specific_properties_are_not_immutable()
    {
        // Base Entity ships RowNumber + CreatedAt as [Immutable] (framework-level invariant); those
        // ride through. The Draft-specific properties (FK + Records) must NOT carry [Immutable]
        // because the Draft layout mutates as the author edits the canvas.
        var immutable = typeof(WorkflowDefinitionDraftLayout).GetImmutableProperties().ToList();

        Assert.DoesNotContain(nameof(WorkflowDefinitionDraftLayout.WorkflowDefinitionDraftId), immutable);
        Assert.DoesNotContain(nameof(WorkflowDefinitionDraftLayout.Records), immutable);
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
}
