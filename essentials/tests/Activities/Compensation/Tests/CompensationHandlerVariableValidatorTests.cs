using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Sequence;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 258 FR-307 (research R6, R18): a scope that can hand over may have its handler run by an enclosing scope after
/// the containers between them have completed, so publishing refuses a handler that uses a variable one of those
/// containers declares. Workflow variables, step results, containers inside the handler and containers above the
/// outermost receiving scope stay available. Uses the real Sequence, Flowchart and loop structure handlers.
/// </summary>
public sealed class CompensationHandlerVariableValidatorTests : IDisposable
{
    private const string VariableKey = "var-total";
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private static readonly VariableDefinition Total = new(VariableKey, "total", new TypeReference("Int32"), null, null);

    private readonly ServiceProvider _provider;
    private readonly CompensationHandlerVariableValidator _validator;

    public CompensationHandlerVariableValidatorTests()
    {
        var services = new ServiceCollection();
        new ActivitiesCompensationFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _validator = new CompensationHandlerVariableValidator(new DefaultActivityStructureService(_provider.GetServices<IActivityStructureHandler>()));
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData(SequenceActivity.StructureKind)]
    [InlineData(FlowchartActivity.StructureKind)]
    public async Task A_handler_reading_a_variable_of_a_container_between_the_scopes_is_reported(string containerKind)
    {
        var root = Scope("outer", ("Order",
            Container(containerKind, "box", [Total], Scope("inner", ("Reserve", Leaf("reserve"), Leaf("release", input: Variable(VariableKey, "box"))))),
            null));

        var error = Assert.Single(await ValidateAsync(root));

        Assert.Equal(("release/inputs/value", CompensationHandlerVariableValidator.HandlerUsesContainerVariableType), (error.Path, error.Type));
        Assert.Contains("'total'", error.Message);
        Assert.Contains("'box'", error.Message);
        Assert.Contains("'outer'", error.Message);
    }

    [Theory]
    [InlineData(ForEachActivity.StructureKind)]
    [InlineData(WhileActivity.StructureKind)]
    [InlineData(ForActivity.StructureKind)]
    public async Task A_handler_reading_a_variable_of_a_container_in_a_loop_body_is_reported(string loopKind)
    {
        // Loops declare no variables of their own; the Sequence in the body does, and is gone once the iteration ends.
        var root = Scope("outer", ("Orders",
            Loop(loopKind, "loop", Container(SequenceActivity.StructureKind, "body", [Total],
                Scope("inner", ("Reserve", Leaf("reserve"), Leaf("release", input: Variable(VariableKey, "body")))))),
            null));

        var error = Assert.Single(await ValidateAsync(root));

        Assert.Equal(("release/inputs/value", CompensationHandlerVariableValidator.HandlerUsesContainerVariableType), (error.Path, error.Type));
    }

    [Fact]
    public async Task An_output_or_a_Set_target_in_a_nested_handler_activity_is_reported()
    {
        var setTotal = Leaf("set") with
        {
            Intrinsic = new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Set, new TypeReference("Int32"), new VariableReference(VariableKey, "box"))
        };
        var refund = Leaf("refund") with
        {
            Outputs = [new ArgumentState("result", new ArgumentValue(new VariableReference(VariableKey, "box"), "Variable"), null, null, null, null)]
        };
        var root = Scope("outer", ("Order",
            Container(SequenceActivity.StructureKind, "box", [Total],
                Scope("inner", ("Reserve", Leaf("reserve"), Container(SequenceActivity.StructureKind, "undo", [], refund, setTotal)))),
            null));

        var errors = await ValidateAsync(root);

        Assert.Equal(["refund/outputs/result", "set/variable"], errors.Select(error => error.Path).Order());
    }

    [Fact]
    public async Task Entries_handed_on_by_a_receiving_scope_put_its_containers_at_risk_too()
    {
        // inner → middle (held in a step) → outer: the handler may run under outer, after "box" completed.
        var root = Scope("outer", ("Order",
            Container(SequenceActivity.StructureKind, "box", [Total],
                Scope("middle", ("Reserve",
                    Scope("inner", ("Hold", Leaf("hold"), Leaf("release", input: Variable(VariableKey, "box")))),
                    null))),
            null));

        var error = Assert.Single(await ValidateAsync(root));

        Assert.Equal("release/inputs/value", error.Path);
    }

    [Fact]
    public async Task A_confirmOnComplete_given_as_an_expression_may_hand_over()
    {
        var inner = Scope("inner", ("Reserve", Leaf("reserve"), Leaf("release", input: Variable(VariableKey, "box"))))
            with { Inputs = [new ArgumentState("confirmOnComplete", new ArgumentValue("return true;", "JavaScript"), null, null, null, null)] };
        var root = Scope("outer", ("Order", Container(SequenceActivity.StructureKind, "box", [Total], inner), null));

        Assert.Single(await ValidateAsync(root));
    }

    [Fact]
    public async Task A_workflow_variable_is_valid()
    {
        var root = Scope("outer", ("Order",
            Container(SequenceActivity.StructureKind, "box", [Total],
                Scope("inner", ("Reserve", Leaf("reserve"), Leaf("release", input: Variable(VariableKey, VariableReference.WorkflowScopeId))))),
            null));

        Assert.Empty(await ValidateAsync(root));
    }

    [Fact]
    public async Task A_step_result_is_valid()
    {
        var result = new ArgumentValue(
            JsonSerializer.SerializeToElement(new { producerNodeId = "reserve", projectionKey = "result", producerScopeId = "box" }, WebOptions),
            "ActivityResult");
        var root = Scope("outer", ("Order",
            Container(SequenceActivity.StructureKind, "box", [Total],
                Scope("inner", ("Reserve", Leaf("reserve"), Leaf("release", input: result)))),
            null));

        Assert.Empty(await ValidateAsync(root));
    }

    [Fact]
    public async Task A_scope_without_an_enclosing_scope_keeps_its_entries()
    {
        var root = Container(SequenceActivity.StructureKind, "box", [Total],
            Scope("inner", ("Reserve", Leaf("reserve"), Leaf("release", input: Variable(VariableKey, "box")))));

        Assert.Empty(await ValidateAsync(root));
    }

    [Fact]
    public async Task A_scope_that_confirms_on_complete_keeps_its_entries()
    {
        var inner = Scope("inner", ("Reserve", Leaf("reserve"), Leaf("release", input: Variable(VariableKey, "box"))))
            with { Inputs = [new ArgumentState("confirmOnComplete", new ArgumentValue(true, "Literal"), null, null, null, null)] };
        var root = Scope("outer", ("Order", Container(SequenceActivity.StructureKind, "box", [Total], inner), null));

        Assert.Empty(await ValidateAsync(root));
    }

    [Theory]
    [InlineData(SequenceActivity.StructureKind)]
    [InlineData(FlowchartActivity.StructureKind)]
    public async Task A_variable_declared_above_the_outer_scope_is_valid(string containerKind)
    {
        var root = Container(containerKind, "box", [Total],
            Scope("outer", ("Order",
                Container(SequenceActivity.StructureKind, "between", [],
                    Scope("inner", ("Reserve", Leaf("reserve"), Leaf("release", input: Variable(VariableKey, "box"))))),
                null)));

        Assert.Empty(await ValidateAsync(root));
    }

    [Fact]
    public async Task A_variable_of_a_container_inside_the_handler_is_valid()
    {
        var root = Scope("outer", ("Order",
            Scope("inner", ("Reserve", Leaf("reserve"),
                Container(SequenceActivity.StructureKind, "undo", [Total], Leaf("release", input: Variable(VariableKey, "undo"))))),
            null));

        Assert.Empty(await ValidateAsync(root));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task A_scope_in_a_handler_is_checked_with_that_handler(bool middleConfirmsOnComplete, int expected)
    {
        // inner sits in middle's handler, so it is part of the compensation middle may hand to outer, after "box"
        // completed. When middle keeps its entries, middle runs it while "box" still runs.
        var middle = Scope("middle", ("Reserve", Leaf("reserve"),
            Scope("inner", ("Release", Leaf("release"), Leaf("undo-release", input: Variable(VariableKey, "box"))))));
        if (middleConfirmsOnComplete)
            middle = middle with { Inputs = [new ArgumentState("confirmOnComplete", new ArgumentValue("true", "Literal"), null, null, null, null)] };
        var root = Scope("outer", ("Order", Container(SequenceActivity.StructureKind, "box", [Total], middle), null));

        var errors = await ValidateAsync(root);

        Assert.Equal(expected, errors.Count);
        Assert.All(errors, error => Assert.Equal("undo-release/inputs/value", error.Path));
    }

    [Fact]
    public async Task A_container_that_is_not_an_ancestor_of_the_handler_is_left_to_the_variable_validator()
    {
        // The step's Sequence is not visible from the handler at all; Expressions/UnresolvedVariable reports it.
        var root = Scope("outer", ("Order",
            Scope("inner", ("Reserve",
                Container(SequenceActivity.StructureKind, "step", [Total], Leaf("reserve")),
                Leaf("release", input: Variable(VariableKey, "step")))),
            null));

        Assert.Empty(await ValidateAsync(root));
    }

    private async Task<IReadOnlyList<ValidationError>> ValidateAsync(ActivityNode root) =>
        [.. await _validator.Validate(new StubDraft(new WorkflowDefinitionState([], root, [], [], null)), CancellationToken.None)];

    private static ArgumentValue Variable(string referenceKey, string declaringScopeId) =>
        new(new VariableReference(referenceKey, declaringScopeId), "Variable");

    private static ActivityNode Leaf(string nodeId, ArgumentValue? input = null) =>
        new(nodeId, "activity-version-leaf", input is null ? [] : [new ArgumentState("value", input, null, null, null, null)], []);

    private static ActivityNode Scope(string nodeId, params (string Name, ActivityNode? Activity, ActivityNode? Compensation)[] steps)
    {
        var structure = new CompensationScopeAuthoredStructure(
            steps.Select(step => new CompensationScopeAuthoredStep(step.Name, step.Activity, step.Compensation)).ToArray());
        return new ActivityNode(nodeId, "activity-version-scope", [], [],
            new ActivityNodeStructure(CompensationScope.StructureKind, CompensationScope.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, WebOptions)));
    }

    // A Sequence or Flowchart: both keep their children in "activities" and their declarations in "variables".
    private static ActivityNode Container(string kind, string nodeId, VariableDefinition[] variables, params ActivityNode[] children) =>
        new(nodeId, $"activity-version-{kind}", [], [],
            new ActivityNodeStructure(kind, "1.0.0", JsonSerializer.SerializeToElement(new { activities = children, variables }, WebOptions)));

    private static ActivityNode Loop(string kind, string nodeId, ActivityNode body) =>
        new(nodeId, $"activity-version-{kind}", [], [],
            new ActivityNodeStructure(kind, "1.0.0", JsonSerializer.SerializeToElement(new { body }, WebOptions)));

    private sealed class StubDraft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
