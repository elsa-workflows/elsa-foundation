using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// #972: a Set/Merge/Reduce intrinsic must target a variable visible from its scope — the draft-side
/// guard of the two-guard pattern (publish backstop:
/// <c>ExecutableNodeCompiler.ValidateIntrinsicVariableTargets</c>). Previously an out-of-scope target
/// published cleanly and poisoned at runtime.
/// </summary>
public sealed class IntrinsicVariableTargetValidatorTests
{
    private static IntrinsicVariableTargetValidator Validator() => new(Options(), Walker(), Resolver());

    [Fact]
    public async Task Set_targeting_a_declared_workflow_variable_emits_no_error()
    {
        var state = State(
            activities: [SetNode("set-1", "var-x", VariableReference.WorkflowScopeId)],
            variables: [Variable("var-x", "X")]);

        Assert.Empty(await Validate(Validator(), state));
    }

    [Fact]
    public async Task Set_targeting_an_undeclared_workflow_variable_emits_an_error()
    {
        var state = State(activities: [SetNode("set-1", "var-missing", VariableReference.WorkflowScopeId)]);

        var error = Assert.Single(await Validate(Validator(), state));
        Assert.Equal("Intrinsics/UnresolvedVariableTarget", error.Type);
        Assert.Equal("set-1/intrinsic/variable", error.Path);
        Assert.Contains("var-missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_targeting_the_enclosing_containers_variable_emits_no_error()
    {
        var container = Node(
            "container-1",
            childActivities: [SetNode("set-1", "var-local", "container-1")],
            containerVariables: [Variable("var-local", "Local")]);
        var state = StateWithRoot(container);

        Assert.Empty(await Validate(Validator(), state));
    }

    [Fact]
    public async Task Set_targeting_a_sibling_containers_variable_emits_an_error()
    {
        // The declaring container is not an ancestor of the Set, so the reference is out of scope
        // (ADR 0027: references are stable, never retargeted through the visible chain).
        var declaring = Node("container-a", containerVariables: [Variable("var-a", "A")]);
        var offending = Node("container-b", childActivities: [SetNode("set-1", "var-a", "container-a")]);
        var root = Node("root", childActivities: [declaring, offending]);
        var state = StateWithRoot(root);

        var error = Assert.Single(await Validate(Validator(), state));
        Assert.Equal("Intrinsics/UnresolvedVariableTarget", error.Type);
        Assert.Contains("container-a", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_variable_intrinsics_are_ignored()
    {
        var finish = new ActivityNode("finish-1", "$intrinsic", [NonVariableOutcomeInput()], [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Finish)
        };
        var state = State(activities: [finish]);

        Assert.Empty(await Validate(Validator(), state));
    }

    private static ActivityNode SetNode(string nodeId, string variableKey, string declaringScopeId) =>
        new(
            nodeId,
            "$intrinsic",
            [new ArgumentState("value", new ArgumentValue("1", "Literal"), null, null, null, null)],
            [])
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(
                AuthoredWorkflowIntrinsicKind.Set,
                new TypeReference("String"),
                new VariableReference(variableKey, declaringScopeId))
        };

    private static ArgumentState NonVariableOutcomeInput() =>
        new("outcome", new ArgumentValue("Done", "Literal"), null, null, null, null);
}
