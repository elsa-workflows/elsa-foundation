using Elsa.Workflows.Design.Core.Models;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-005 + Unit C FR-009: after the rename, zero occurrences of
/// <c>ActivityNode.ReferenceKey</c> remains in the Workflows.Design model surface, and the
/// provisional flowchart-specific <c>ActivityPortConnection</c> model is no longer in core.
/// Argument-level <c>ReferenceKey</c>
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
    public void WorkflowsDesignCore_has_no_ActivityPortConnection_model()
    {
        var assembly = typeof(ActivityNode).Assembly;

        Assert.Null(assembly.GetType("Elsa.Workflows.Design.Core.Models.ActivityPortConnection"));
        Assert.Null(assembly.GetType("Elsa.Workflows.Design.Core.Models.ActivityConnection"));
    }
}
