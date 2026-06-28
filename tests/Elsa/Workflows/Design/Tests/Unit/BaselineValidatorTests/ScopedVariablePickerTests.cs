using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// Backend picker contract for scoped variables (#208): given a selected activity, the picker
/// surfaces only the variables visible from that activity's scope, nearest-scope first.
/// </summary>
public sealed class ScopedVariablePickerTests
{
    [Fact]
    public void Picker_returns_workflow_and_visible_container_variables_for_a_descendant()
    {
        var child = Node("child");
        var container = Node("container", childActivities: [child], containerVariables: [Variable("var-c", "Local")]);
        var state = State(activities: [container], variables: [Variable("var-wf", "WorkflowVar")]);

        var visible = Picker().GetVisibleVariables(state, "child");

        // Nearest scope (container) first, then workflow scope.
        Assert.Collection(visible,
            v => { Assert.Equal("container", v.ScopeId); Assert.False(v.IsWorkflowScope); Assert.Equal("var-c", v.Variable.ReferenceKey); },
            v => { Assert.True(v.IsWorkflowScope); Assert.Equal("var-wf", v.Variable.ReferenceKey); });
    }

    [Fact]
    public void Picker_hides_container_variables_from_activities_outside_the_container()
    {
        var container = Node("container", childActivities: [Node("inner")], containerVariables: [Variable("var-c", "Local")]);
        var outsider = Node("outsider");
        var state = State(activities: [container, outsider], variables: [Variable("var-wf", "WorkflowVar")]);

        var visible = Picker().GetVisibleVariables(state, "outsider");

        var only = Assert.Single(visible);
        Assert.True(only.IsWorkflowScope);
        Assert.Equal("var-wf", only.Variable.ReferenceKey);
    }

    [Fact]
    public void Picker_truncates_beyond_max_recursion_depth()
    {
        // grandchild sits below maxDepth=1, so the resolver never records its visible scopes and the
        // picker returns nothing rather than walking unbounded into malformed/deep trees.
        var grandchild = Node("grandchild");
        var child = Node("child", childActivities: [grandchild], containerVariables: [Variable("var-c", "Local")]);
        var container = Node("container", childActivities: [child], containerVariables: [Variable("var-o", "Outer")]);
        var state = State(activities: [container], variables: [Variable("var-wf", "WorkflowVar")]);

        Assert.Empty(Picker().GetVisibleVariables(state, "grandchild", maxDepth: 1));
    }

    [Fact]
    public void Structure_service_reports_container_capability()
    {
        var structureService = StructureService();
        var container = Node("container", containerVariables: [Variable("var-c", "Local")]);
        var plain = Node("plain");

        Assert.True(structureService.SupportsScopedVariables(container));
        Assert.False(structureService.SupportsScopedVariables(plain));
    }
}
