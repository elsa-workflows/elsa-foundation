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
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_resolving_known_referenceKey_emits_no_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "var-1")])],
            variables: [Variable("var-1", "MyVar")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_with_structured_workflow_reference_resolving_known_referenceKey_emits_no_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", new VariableReference("var-1"))])],
            variables: [Variable("var-1", "MyVar")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_with_structured_workflow_reference_lookup_is_by_ReferenceKey_not_Name()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", new VariableReference("var-1", VariableReference.WorkflowScopeId))])],
            variables: [Variable("var-1", "DifferentName")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

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
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Variable_expression_resolving_unknown_referenceKey_emits_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "var-missing")])],
            variables: [Variable("var-1", "MyVar")]
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

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
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

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
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

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
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

        Assert.Contains(errors, e => e.Path == "child/inputs/body");
    }

    [Fact]
    public async Task Variable_expression_on_output_emits_error_with_outputs_path()
    {
        var state = State(
            activities: [Node("n1", outputs: [VariableInput("result", "var-missing")])],
            variables: []
        );
        var errors = await Validate(new VariableExpressionResolverValidator(Options(), Walker()), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1/outputs/result", error.Path);
    }
}
