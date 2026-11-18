using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.Liquid;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>
/// Spec 1222 User Story 4 (T034, T036): a rename updates the script references it can resolve, keeps readable names in
/// source, and surfaces the rest for manual repair, never silently breaking or retargeting a read.
/// </summary>
public sealed class ScriptVariableRenamePlannerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ScriptVariableRenamePlanner _planner;

    public ScriptVariableRenamePlannerTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new LiquidExpressionsFeature().ConfigureServices(services);
        services.AddSingleton<IActivityStructureService, DefaultActivityStructureService>();
        _provider = services.BuildServiceProvider();
        var scope = _provider.CreateScope();
        _planner = new ScriptVariableRenamePlanner(
            new ScriptVariableReadResolver(scope.ServiceProvider.GetServices<IScriptVariableReadAnalyzer>()),
            _provider.GetRequiredService<IActivityStructureService>());
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData("variables.order.total + 1", "variables.purchase.total + 1")]
    [InlineData("getVariable('order').total", "getVariable(\"purchase\").total")]
    [InlineData("variables['order'] ?? variables.order", "variables[\"purchase\"] ?? variables.purchase")]
    [InlineData("'variables.order' + args.order", null)]
    public async Task A_resolvable_literal_read_follows_the_rename(string source, string? expected)
    {
        var plan = await PlanAsync(State(Node("a", Js(source))), "order-key", "purchase");

        if (expected is null)
            Assert.Empty(plan.Edits);
        else
            Assert.Equal(expected, Assert.Single(plan.Edits).NewSource);
        Assert.Empty(plan.Flags);
    }

    [Fact]
    public async Task A_read_that_resolves_to_a_shadowing_container_variable_is_left_alone()
    {
        var inner = new VariableDefinition("inner-order", "order", new TypeReference("Any"), null, null);

        var plan = await PlanAsync(State(Container(inner, Node("a", Js("variables.order")))), "order-key", "purchase");

        Assert.Empty(plan.Edits);
    }

    [Fact]
    public async Task A_computed_read_that_could_reach_the_variable_is_flagged()
    {
        var plan = await PlanAsync(State(Node("a", Js("variables[key]"))), "order-key", "purchase");

        Assert.Empty(plan.Edits);
        Assert.Equal(ScriptVariableRenameFlagReason.ComputedRead, Assert.Single(plan.Flags).Reason);
    }

    [Fact]
    public async Task A_liquid_read_is_flagged_for_manual_repair()
    {
        var plan = await PlanAsync(
            State(Node("a", new ArgumentState("Text", new ArgumentValue("{{ variables.order }}", "Liquid"), null, null, null, null))),
            "order-key",
            "purchase");

        Assert.Equal(ScriptVariableRenameFlagReason.NoSourcePositions, Assert.Single(plan.Flags).Reason);
    }

    [Fact]
    public async Task A_new_name_that_is_not_an_identifier_is_flagged_for_a_member_read()
    {
        var plan = await PlanAsync(State(Node("a", Js("variables.order"))), "order-key", "order-2");

        Assert.Equal(ScriptVariableRenameFlagReason.NameNotAnIdentifier, Assert.Single(plan.Flags).Reason);
    }

    [Fact]
    public async Task A_read_of_the_new_name_that_the_rename_would_capture_is_flagged()
    {
        // The script reads a workflow variable named purchase; a container's order renamed to purchase would capture it.
        var inner = new VariableDefinition("inner-order", "order", new TypeReference("Any"), null, null);
        var state = State(Container(inner, Node("a", Js("variables.purchase"))), new VariableDefinition("purchase-key", "purchase", new TypeReference("Any"), null, null));

        var plan = await _planner.PlanAsync(state, "container", "inner-order", "purchase");

        Assert.Equal(ScriptVariableRenameFlagReason.Captured, Assert.Single(plan.Flags).Reason);
    }

    [Fact]
    public async Task A_script_body_is_planned_when_its_input_is_named()
    {
        var node = Node("script", new ArgumentState("script", new ArgumentValue("return variables.order.total;", "Literal"), null, null, null, null));

        var plan = await _planner.PlanAsync(State(node), VariableReference.WorkflowScopeId, "order-key", "purchase",
            (_, _) => ValueTask.FromResult<string?>("script"));

        Assert.Equal("return variables.purchase.total;", Assert.Single(plan.Edits).NewSource);
    }

    private Task<ScriptVariableRenamePlan> PlanAsync(WorkflowDefinitionState state, string key, string newName) =>
        _planner.PlanAsync(state, VariableReference.WorkflowScopeId, key, newName).AsTask();

    private static WorkflowDefinitionState State(ActivityNode root, params VariableDefinition[] extra) =>
        new([new VariableDefinition("order-key", "order", new TypeReference("Any"), null, null), .. extra], root, [], [], null);

    private static ArgumentState Js(string source) => new("Text", new ArgumentValue(source, "JavaScript"), null, null, null, null);

    private static ActivityNode Node(string nodeId, ArgumentState input) => new(nodeId, "activity-version-write", [input], []);

    private static ActivityNode Container(VariableDefinition variable, params ActivityNode[] children) =>
        new("container", "activity-version-sequence", [], [])
        {
            Structure = new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(children, [variable]), new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        };
}
