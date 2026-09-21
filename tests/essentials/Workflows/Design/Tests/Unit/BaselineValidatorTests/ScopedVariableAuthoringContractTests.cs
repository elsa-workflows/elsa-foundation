using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// Backend wire contract for scoped-variable authoring (#213). The external Studio UI lives in a
/// separate repository; here we cover the backend contract it consumes: visible-variable picker
/// views, non-blocking shadowing warnings, and invalid-reference diagnostics over the wire.
/// </summary>
public sealed class ScopedVariableAuthoringContractTests
{
    [Fact]
    public void Visible_variables_view_presents_workflow_and_container_declarations_uniformly()
    {
        var child = Node("child");
        var container = Node("container", childActivities: [child], containerVariables: [Variable("var-c", "Local")]);
        var state = State(activities: [container], variables: [Variable("var-wf", "WorkflowVar")]);

        var visible = Authoring().GetVisibleVariables(state, "child");

        Assert.Collection(visible,
            v => { Assert.Equal("var-c", v.ReferenceKey); Assert.Equal("container", v.ScopeId); Assert.False(v.IsWorkflowScope); },
            v => { Assert.Equal("var-wf", v.ReferenceKey); Assert.True(v.IsWorkflowScope); });
    }

    [Fact]
    public void Visible_variable_view_is_json_serializable_for_the_wire()
    {
        var state = State(activities: [Node("n1")], variables: [Variable("var-wf", "WorkflowVar")]);

        var view = Assert.Single(Authoring().GetVisibleVariables(state, "n1"));
        var roundTripped = JsonSerializer.Deserialize<VisibleVariableView>(JsonSerializer.Serialize(view));

        Assert.Equal(view, roundTripped);
    }

    [Fact]
    public void Null_node_selection_has_no_visible_variables_but_preserves_state_wide_shadowing_warnings()
    {
        var inner = Node("inner", childActivities: [Node("leaf")], containerVariables: [Variable("var-inner", "Counter")]);
        var state = State(activities: [inner], variables: [Variable("var-wf", "Counter")]);
        var authoring = Authoring();

        var visible = authoring.GetVisibleVariables(state, null);
        var warnings = authoring.GetShadowingWarnings(state);

        Assert.Empty(visible);
        Assert.Single(warnings);
    }

    [Fact]
    public void Shadowing_produces_a_non_blocking_warning_but_not_a_validation_error()
    {
        // Inner container declares "Counter", shadowing the workflow-scoped "Counter".
        var inner = Node("inner", childActivities: [Node("leaf")], containerVariables: [Variable("var-inner", "Counter")]);
        var state = State(activities: [inner], variables: [Variable("var-wf", "Counter")]);

        var warnings = Authoring().GetShadowingWarnings(state);

        var warning = Assert.Single(warnings);
        Assert.Equal("inner", warning.ScopeId);
        Assert.Equal(VariableReference.WorkflowScopeId, warning.ShadowedScopeId);
        Assert.Equal("Counter", warning.Name);
    }

    [Fact]
    public async Task Shadowing_is_allowed_so_validation_emits_no_error()
    {
        var inner = Node("inner", childActivities: [Node("leaf")], containerVariables: [Variable("var-inner", "Counter")]);
        var state = State(activities: [inner], variables: [Variable("var-wf", "Counter")]);

        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public void No_shadowing_warning_when_names_are_distinct_across_scopes()
    {
        var inner = Node("inner", childActivities: [Node("leaf")], containerVariables: [Variable("var-inner", "Local")]);
        var state = State(activities: [inner], variables: [Variable("var-wf", "WorkflowVar")]);

        Assert.Empty(Authoring().GetShadowingWarnings(state));
    }
}
