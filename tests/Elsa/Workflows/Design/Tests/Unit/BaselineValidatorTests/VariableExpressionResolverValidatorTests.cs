using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// SC-022(f). Branch coverage — non-Variable expression, known variable, unknown variable,
/// null reference, recursion into ChildActivities.
/// </summary>
public sealed class VariableExpressionResolverValidatorTests
{
    [Fact]
    public async Task Non_Variable_expression_emits_no_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [LiteralInput("body", "hello")])],
            variables: []
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_resolving_known_referenceKey_emits_no_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "var-1")])],
            variables: [Variable("var-1", "MyVar")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_with_structured_workflow_reference_resolving_known_referenceKey_emits_no_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", new VariableReference("var-1"))])],
            variables: [Variable("var-1", "MyVar")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_with_structured_workflow_reference_lookup_is_by_ReferenceKey_not_Name()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", new VariableReference("var-1", VariableReference.WorkflowScopeId))])],
            variables: [Variable("var-1", "DifferentName")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_with_json_workflow_reference_resolving_known_referenceKey_emits_no_error()
    {
        var reference = JsonSerializer.SerializeToElement(new { referenceKey = "var-1", declaringScopeId = VariableReference.WorkflowScopeId });
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", reference)])],
            variables: [Variable("var-1", "MyVar")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_resolving_unknown_referenceKey_emits_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "var-missing")])],
            variables: [Variable("var-1", "MyVar")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1/inputs/body", error.Path);
        Assert.Equal("Expressions/UnresolvedVariable", error.Type);
        Assert.Contains("var-missing", error.Message);
    }

    [Fact]
    public async Task Variable_expression_with_empty_reference_emits_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "")])],
            variables: []
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Single(errors);
    }

    [Fact]
    public async Task Variable_expression_lookup_is_by_ReferenceKey_not_Name()
    {
        // Name and ReferenceKey differ; the validator must compare by ReferenceKey.
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "var-1")])],
            variables: [Variable("var-1", "DifferentName")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_on_nested_child_activity_emits_error()
    {
        var child = Node("child", inputs: [VariableInput("body", "var-missing")]);
        var root = Node("root", childActivities: [child]);
        var state = State(
            activities: [root],
            variables: []
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Contains(errors, e => e.Path == "child/inputs/body");
    }

    [Fact]
    public async Task Variable_expression_on_output_emits_error_with_outputs_path()
    {
        var state = State(
            activities: [Node("n1", outputs: [VariableInput("result", "var-missing")])],
            variables: []
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1/outputs/result", error.Path);
    }

    [Fact]
    public async Task Descendant_reference_to_visible_container_variable_emits_no_error()
    {
        // A child reads a container-scoped variable declared by its ancestor container (#207).
        var child = Node("child", inputs: [VariableInput("body", new VariableReference("var-c", "container"))]);
        var container = Node("container", childActivities: [child], containerVariables: [Variable("var-c", "Local")]);
        var state = State(activities: [container], variables: []);

        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Reference_to_container_variable_from_outside_that_container_emits_error()
    {
        // A sibling outside the declaring container cannot see its container-scoped variable.
        var container = Node("container", childActivities: [Node("inner")], containerVariables: [Variable("var-c", "Local")]);
        var outsider = Node("outsider", inputs: [VariableInput("body", new VariableReference("var-c", "container"))]);
        var state = State(activities: [container, outsider], variables: []);

        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        var error = Assert.Single(errors);
        Assert.Equal("outsider/inputs/body", error.Path);
        Assert.Equal("Expressions/UnresolvedVariable", error.Type);
    }

    [Fact]
    public async Task Workflow_variable_remains_visible_inside_a_container()
    {
        var child = Node("child", inputs: [VariableInput("body", new VariableReference("var-wf", VariableReference.WorkflowScopeId))]);
        var container = Node("container", childActivities: [child], containerVariables: [Variable("var-c", "Local")]);
        var state = State(activities: [container], variables: [Variable("var-wf", "WorkflowVar")]);

        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Descendant_assignment_to_visible_container_variable_emits_no_error()
    {
        // An assignment is a variable reference on an output; assigning a visible ancestor container
        // variable is allowed (#209).
        var child = Node("child", outputs: [VariableInput("result", new VariableReference("var-c", "container"))]);
        var container = Node("container", childActivities: [child], containerVariables: [Variable("var-c", "Local")]);
        var state = State(activities: [container], variables: []);

        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Assignment_to_a_sibling_container_variable_emits_error()
    {
        // A child of container A cannot assign a variable declared by sibling container B (#209).
        var childA = Node("child-a", outputs: [VariableInput("result", new VariableReference("var-b", "container-b"))]);
        var containerA = Node("container-a", childActivities: [childA], containerVariables: [Variable("var-a", "A")]);
        var containerB = Node("container-b", childActivities: [Node("child-b")], containerVariables: [Variable("var-b", "B")]);
        var state = State(activities: [containerA, containerB], variables: []);

        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        var error = Assert.Single(errors);
        Assert.Equal("child-a/outputs/result", error.Path);
    }

    [Fact]
    public async Task Nested_containers_allow_shadowing_and_resolve_each_scope_explicitly()
    {
        // Outer and inner containers both declare the same reference key in their own scope.
        // A deeply nested child can reference either scope explicitly; both are visible.
        var leafInner = Node("leaf", inputs: [VariableInput("body", new VariableReference("var-x", "inner"))]);
        var leafOuter = Node("leaf2", inputs: [VariableInput("body", new VariableReference("var-x", "outer"))]);
        var inner = Node("inner", childActivities: [leafInner, leafOuter], containerVariables: [Variable("var-x", "Inner")]);
        var outer = Node("outer", childActivities: [inner], containerVariables: [Variable("var-x", "Outer")]);
        var state = State(activities: [outer], variables: []);

        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker(), Resolver()), state);

        Assert.Empty(errors);
    }
}
