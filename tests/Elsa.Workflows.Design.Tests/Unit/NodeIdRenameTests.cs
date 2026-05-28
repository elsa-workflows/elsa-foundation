using Elsa.Workflows.Design.Core.Models;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-005 + Unit C FR-009: after the rename, zero occurrences of
/// <c>ActivityNode.ReferenceKey</c> or <c>ActivityPortConnection.ActivityReferenceKey</c>
/// remain in the Workflows.Design model surface. Argument-level <c>ReferenceKey</c>
/// identifiers (per FR-010 — on <c>ArgumentDefinition</c>, <c>InputDefinition</c>,
/// <c>OutputDefinition</c>, <c>VariableDefinition</c>) are deliberately unchanged.
/// </summary>
public sealed class NodeIdRenameTests
{
    [Fact]
    public void ActivityNode_has_no_ReferenceKey_property()
    {
        var members = typeof(ActivityNode)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.DoesNotContain("ReferenceKey", members);
    }

    [Fact]
    public void ActivityNode_has_NodeId_property()
    {
        var members = typeof(ActivityNode)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.Contains(nameof(ActivityNode.NodeId), members);
    }

    [Fact]
    public void ActivityPortConnection_has_no_ActivityReferenceKey_property()
    {
        var members = typeof(ActivityPortConnection)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.DoesNotContain("ActivityReferenceKey", members);
    }

    [Fact]
    public void ActivityPortConnection_has_ActivityNodeId_property()
    {
        var members = typeof(ActivityPortConnection)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name);

        Assert.Contains(nameof(ActivityPortConnection.ActivityNodeId), members);
    }
}
