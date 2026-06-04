using Elsa.Workflows.Design.Persistence.Core.Entities;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.LayoutEntityTests;

/// <summary>
/// SC-021 + framework §2.9 + Unit C FR-006a: <c>WorkflowDefinitionVersionLayout</c> mirrors
/// <c>WorkflowDefinitionVersion</c>'s immutability regime. Write-once properties are enforced
/// via <c>PropertySaveBehavior.Throw</c> in <c>WorkflowDefinitionVersionLayoutConfiguration</c>
/// (verified by the integration test <c>CrossContextLifecycleTests</c>).
/// </summary>
public sealed class VersionLayoutImmutabilityTests
{
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
