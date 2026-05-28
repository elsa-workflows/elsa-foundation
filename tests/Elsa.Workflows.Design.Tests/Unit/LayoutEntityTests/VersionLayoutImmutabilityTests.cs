using Elsa.Primitives.Attributes;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.LayoutEntityTests;

/// <summary>
/// SC-021 + framework §2.9 + Unit C FR-006a: <c>WorkflowDefinitionVersionLayout</c> mirrors
/// <c>WorkflowDefinitionVersion</c>'s immutability regime. The <c>[Immutable]</c> scanner
/// (Elsa.Persistence.EFCore's <c>ApplyImmutability</c>) picks the marked properties up and
/// drives <c>PropertySaveBehavior.Throw</c> at the provider layer.
/// </summary>
public sealed class VersionLayoutImmutabilityTests
{
    [Fact]
    public void WorkflowDefinitionVersionId_is_marked_immutable()
    {
        var immutable = typeof(WorkflowDefinitionVersionLayout).GetImmutableProperties().ToList();

        Assert.Contains(nameof(WorkflowDefinitionVersionLayout.WorkflowDefinitionVersionId), immutable);
    }

    [Fact]
    public void Records_is_marked_immutable()
    {
        var immutable = typeof(WorkflowDefinitionVersionLayout).GetImmutableProperties().ToList();

        Assert.Contains(nameof(WorkflowDefinitionVersionLayout.Records), immutable);
    }

    [Fact]
    public void Records_property_uses_init_only_setter()
    {
        var setter = typeof(WorkflowDefinitionVersionLayout)
            .GetProperty(nameof(WorkflowDefinitionVersionLayout.Records))!
            .SetMethod;

        Assert.NotNull(setter);
        // init-only accessors carry the IsExternalInit modifier
        var hasInitModifier = setter!.ReturnParameter.GetRequiredCustomModifiers()
            .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        Assert.True(hasInitModifier, "Records setter must be init-only (mirrors immutability regime)");
    }

    [Fact]
    public void Entity_is_sealed()
    {
        Assert.True(typeof(WorkflowDefinitionVersionLayout).IsSealed);
    }
}
