using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.ControlFlow.Tests;

/// <summary>
/// Spec 1222 FR-001, FR-004a, FR-009 (T006): publish resolves a script's literal variable name nearest scope first, loop
/// iteration values innermost, then containers, then the workflow; a computed read is reported with every variable it
/// could reach, each name once.
/// </summary>
public sealed class ScriptVariableReadResolutionTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;
    private readonly ScriptVariableReadResolver _resolver;

    public ScriptVariableReadResolutionTests()
    {
        var services = new ServiceCollection();
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        services.AddSingleton<IActivityStructureService, DefaultActivityStructureService>();
        _provider = services.BuildServiceProvider();
        _structures = _provider.GetRequiredService<IActivityStructureService>();
        _resolver = new ScriptVariableReadResolver(_provider.GetServices<IScriptVariableReadAnalyzer>());
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void A_workflow_variable_resolves_to_the_workflow_scope()
    {
        var plan = Resolve("variables.order.total", Body(), "script");

        var read = Assert.Single(plan.Pinned);
        Assert.Equal((VariableReference.WorkflowScopeId, "order-key"), (read.ScopeId, read.Variable.ReferenceKey));
        Assert.Empty(plan.Unresolved);
        Assert.Null(plan.Fallback());
    }

    [Fact]
    public void A_container_variable_shadows_the_workflow_variable()
    {
        var plan = Resolve("getVariable('order')", Container(Var("order", "inner-order"), Body()), "script");

        Assert.Equal(("container", "inner-order"), (Assert.Single(plan.Pinned).ScopeId, plan.Pinned[0].Variable.ReferenceKey));
    }

    [Fact]
    public void A_foreach_body_reads_the_loop_item_as_a_variable_of_the_loop_node()
    {
        // FR-004a: the iteration scope is innermost, so it wins over a workflow variable of the same name.
        var plan = Resolve("variables.currentItem.id + variables.currentIndex", ForEach(Body()), "script", Var("currentItem", "wf-current"));

        Assert.Equal(
            [("loop", "currentItem"), ("loop", "currentIndex")],
            plan.Pinned.Select(read => (read.ScopeId, read.Variable.ReferenceKey)));
        Assert.All(plan.Pinned, read => Assert.True(read.IsIterationValue));
        Assert.Equal(
            ["variables.currentIndex", "variables.currentItem"],
            plan.PinnedParameters().Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ExpressionParameterSurfaces.Variables, plan.PinnedParameters()["variables.currentItem"].Surface);
    }

    [Fact]
    public void A_for_body_reads_its_index()
    {
        var plan = Resolve("variables.index", Loop(ForActivity.StructureKind, ForActivity.StructureSchemaVersion, ForActivity.BodySlotName, Body()), "script");

        Assert.Equal(("loop", "index"), (Assert.Single(plan.Pinned).ScopeId, plan.Pinned[0].Variable.ReferenceKey));
    }

    [Fact]
    public void A_while_loop_has_no_iteration_values()
    {
        var plan = Resolve("variables.index", Loop(WhileActivity.StructureKind, WhileActivity.StructureSchemaVersion, WhileActivity.BodySlotName, Body()), "script");

        Assert.Equal(["index"], plan.Unresolved);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData(null, true)]
    public void CurrentIndex_is_declared_unless_expose_index_is_the_literal_false(string? exposeIndex, bool declared)
    {
        var inputs = exposeIndex is null
            ? Array.Empty<ArgumentState>()
            : [new ArgumentState(nameof(ForEachActivity.ExposeIndex), new ArgumentValue(JsonSerializer.SerializeToElement(bool.Parse(exposeIndex)), "Literal"), null, null, null, null)];

        var plan = Resolve("variables.currentIndex", ForEach(Body(), inputs), "script");

        Assert.Equal(declared, plan.Unresolved.Count == 0);
    }

    [Fact]
    public void An_expose_index_expression_still_declares_current_index()
    {
        // Resolved only at run time; a pass without an index faults the pinned read there (FR-004a).
        var input = new ArgumentState(nameof(ForEachActivity.ExposeIndex), new ArgumentValue("args.flag", "JavaScript"), null, null, null, null);

        var plan = Resolve("variables.currentIndex", ForEach(Body(), [input]), "script");

        Assert.Empty(plan.Unresolved);
    }

    [Fact]
    public void A_name_outside_the_loop_body_does_not_see_its_iteration_values()
    {
        var root = Container(null, ForEach(Body("inside")), Body("after"));

        Assert.Empty(Resolve("variables.currentItem", root, "inside").Unresolved);
        Assert.Equal(["currentItem"], Resolve("variables.currentItem", root, "after").Unresolved);
    }

    [Fact]
    public void A_computed_read_reports_every_reachable_variable_nearest_first_each_name_once()
    {
        var plan = Resolve("variables[key] + variables.order", Container(Var("order", "inner-order"), ForEach(Body())), "script", Var("customer", "customer-key"));

        Assert.Equal(("container", "inner-order"), (Assert.Single(plan.Pinned).ScopeId, plan.Pinned[0].Variable.ReferenceKey));
        var candidates = plan.Fallback()!.Candidates;
        Assert.Equal(
            ["currentItem", "currentIndex", "order", "customer"],
            candidates.Select(candidate => candidate.Name));
        Assert.Equal(("container", "inner-order"), (candidates[2].DeclaringScopeNodeId, candidates[2].VariableKey));
    }

    [Fact]
    public void An_unresolvable_literal_name_is_reported()
    {
        Assert.Equal(["missing"], Resolve("variables.missing", Body(), "script").Unresolved);
    }

    [Fact]
    public void A_body_may_write_what_it_writes_and_the_object_values_that_escape()
    {
        // Spec 1231 FR-019: a written variable is in the may-write set whatever its type; an escaped scalar is not, since a
        // script cannot change it in place; a loop item is frozen, so it is never written.
        var plan = Resolve(
            "variables.count = 1; const o = variables.order; const n = variables.name; helper(variables.currentItem);",
            ForEach(Body()), "script", scriptBody: true,
            new VariableDefinition("count-key", "count", new TypeReference("Int32"), null, null),
            new VariableDefinition("name-key", "name", new TypeReference("String"), null, null));

        Assert.Equal(["count", "order"], plan.MayWrite().Select(write => write.Name));
        Assert.Equal((VariableReference.WorkflowScopeId, "order-key"), (plan.MayWrite()[1].ScopeId, plan.MayWrite()[1].Variable.ReferenceKey));
        Assert.False(plan.HasComputedWrite);
    }

    [Fact]
    public void A_write_to_a_loop_item_is_resolved_to_the_iteration_value()
    {
        var plan = Resolve("variables.currentItem.total = 1;", ForEach(Body()), "script", scriptBody: true);

        Assert.Equal("currentItem", Assert.Single(plan.Writes).Name);
        Assert.True(Assert.Single(plan.Pinned).IsIterationValue);
        Assert.Empty(plan.MayWrite());
    }

    [Fact]
    public void A_computed_write_without_any_read_is_still_reported()
    {
        var plan = Resolve("setVariable(k, 1);", Body(), "script", scriptBody: true);

        Assert.True(plan.HasComputedWrite);
        Assert.Empty(plan.MayWrite());
    }

    private ScriptVariableReadPlan Resolve(string source, ActivityNode root, string nodeId, params VariableDefinition[] extraWorkflowVariables) =>
        Resolve(source, root, nodeId, scriptBody: false, extraWorkflowVariables);

    private ScriptVariableReadPlan Resolve(string source, ActivityNode root, string nodeId, bool scriptBody, params VariableDefinition[] extraWorkflowVariables)
    {
        var visibility = new ScopedVariableResolver(_structures)
            .Resolve([Var("order", "order-key"), .. extraWorkflowVariables], root, 50, includeIterationScopes: true);
        return _resolver.Resolve("JavaScript", source, visibility.GetVisibleScopes(nodeId), scriptBody)!;
    }

    private static VariableDefinition Var(string name, string key) => new(key, name, new TypeReference("Any"), null, null);

    private static ActivityNode Body(string nodeId = "script") => new(nodeId, "activity-version-script", [], []);

    private static ActivityNode Container(VariableDefinition? variable, params ActivityNode[] children) =>
        new("container", "activity-version-sequence", [], [])
        {
            Structure = new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new SequenceAuthoredStructure(children, variable is null ? [] : [variable]),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        };

    private ActivityNode ForEach(ActivityNode body, IEnumerable<ArgumentState>? inputs = null) =>
        Loop(ForEachActivity.StructureKind, ForEachActivity.StructureSchemaVersion, ForEachActivity.BodySlotName, body, inputs);

    private ActivityNode Loop(string kind, string schemaVersion, string bodySlot, ActivityNode body, IEnumerable<ArgumentState>? inputs = null)
    {
        var handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), handler => handler.Kind == kind);
        var node = new ActivityNode("loop", "activity-version-loop", inputs ?? [], []) { Structure = new ActivityNodeStructure(kind, schemaVersion, JsonSerializer.SerializeToElement(new { })) };
        return handler.ReplaceChildren(node, [new ActivityChildProjection(bodySlot, [body])]);
    }
}
