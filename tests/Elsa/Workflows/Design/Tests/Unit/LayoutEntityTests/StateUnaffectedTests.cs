using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.LayoutEntityTests;

/// <summary>
/// SC-004 + Elsa §E2.9.2: layout is owned by the Layout siblings and is NOT reachable
/// through <see cref="WorkflowDefinitionState"/>. State has no layout-typed members.
/// </summary>
public sealed class StateUnaffectedTests
{
    [Fact]
    public void WorkflowDefinitionState_has_no_layout_typed_members()
    {
        var stateMembers = typeof(WorkflowDefinitionState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.PropertyType);

        foreach (var memberType in stateMembers)
        {
            var unwrapped = UnwrapCollection(memberType);
            Assert.NotEqual(typeof(WorkflowDefinitionVersionLayout), unwrapped);
            Assert.NotEqual(typeof(WorkflowDefinitionDraftLayout), unwrapped);
            Assert.NotEqual(typeof(DesignMetadataRecord), unwrapped);
        }
    }

    [Fact]
    public void WorkflowDefinitionState_member_names_carry_no_layout_naming()
    {
        var stateMembers = typeof(WorkflowDefinitionState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        foreach (var name in stateMembers)
        {
            Assert.DoesNotContain("Layout", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DesignMetadata", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Type UnwrapCollection(Type t)
    {
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(List<>) || def == typeof(IList<>) || def == typeof(ICollection<>))
                return t.GetGenericArguments()[0];
        }
        return t;
    }
}
