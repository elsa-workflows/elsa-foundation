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
        var evt = EventFor(state);

        await new VariableExpressionResolverValidator(Options()).Handle(evt, CancellationToken.None);

        Assert.Empty(evt.Errors);
    }

    [Fact]
    public async Task Variable_expression_resolving_known_referenceKey_emits_no_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "var-1")])],
            variables: [Variable("var-1", "MyVar")]
        );
        var evt = EventFor(state);

        await new VariableExpressionResolverValidator(Options()).Handle(evt, CancellationToken.None);

        Assert.Empty(evt.Errors);
    }

    [Fact]
    public async Task Variable_expression_resolving_unknown_referenceKey_emits_error()
    {
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "var-missing")])],
            variables: [Variable("var-1", "MyVar")]
        );
        var evt = EventFor(state);

        await new VariableExpressionResolverValidator(Options()).Handle(evt, CancellationToken.None);

        var error = Assert.Single(evt.Errors);
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
        var evt = EventFor(state);

        await new VariableExpressionResolverValidator(Options()).Handle(evt, CancellationToken.None);

        Assert.Single(evt.Errors);
    }

    [Fact]
    public async Task Variable_expression_lookup_is_by_ReferenceKey_not_Name()
    {
        // Name and ReferenceKey differ; the validator must compare by ReferenceKey.
        var state = State(
            activities: [Node("n1", inputs: [VariableInput("body", "var-1")])],
            variables: [Variable("var-1", "DifferentName")]
        );
        var evt = EventFor(state);

        await new VariableExpressionResolverValidator(Options()).Handle(evt, CancellationToken.None);

        Assert.Empty(evt.Errors);
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
        var evt = EventFor(state);

        await new VariableExpressionResolverValidator(Options()).Handle(evt, CancellationToken.None);

        Assert.Contains(evt.Errors, e => e.Path == "child/inputs/body");
    }

    [Fact]
    public async Task Variable_expression_on_output_emits_error_with_outputs_path()
    {
        var state = State(
            activities: [Node("n1", outputs: [VariableInput("result", "var-missing")])],
            variables: []
        );
        var evt = EventFor(state);

        await new VariableExpressionResolverValidator(Options()).Handle(evt, CancellationToken.None);

        var error = Assert.Single(evt.Errors);
        Assert.Equal("n1/outputs/result", error.Path);
    }
}
