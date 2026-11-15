using System.Text.Json;
using Elsa.Activities.If.Authoring;
using Elsa.Activities.If.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Authoring;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class WorkflowBuilderTests
{
    [Fact]
    public void Definition_builds_once_per_version_and_sequence_preserves_call_order()
    {
        var definition = new TestWorkflow();

        var first = definition.Compile("version-1");
        var second = definition.Compile("version-1");

        Assert.Same(first, second);
        Assert.Equal(1, definition.BuildCount);
        Assert.Equal(["write-1", "write-2", "intrinsic-3"], Children(first).Select(node => node.NodeId));
    }

    [Fact]
    public void Arguments_distinguish_source_literal_null_default_and_omitted()
    {
        var state = new ArgumentWorkflow().Compile();
        var node = Assert.Single(Children(state));
        var inputs = node.Inputs.ToDictionary(input => input.ReferenceKey);

        Assert.Equal("WorkflowRequest", inputs["source"].Value.ExpressionType);
        Assert.Equal("Literal", inputs["literal"].Value.ExpressionType);
        Assert.Equal("Literal", inputs["null"].Value.ExpressionType);
        Assert.Null(inputs["null"].Value.Value);
        Assert.Equal("Default", inputs["default"].Value.ExpressionType);
        Assert.DoesNotContain("omitted", inputs.Keys);
    }

    [Fact]
    public void Call_handle_separates_node_result_projection_and_outcome()
    {
        var definition = new HandleWorkflow();
        definition.Compile();

        Assert.Equal("probe", definition.Call!.Node.NodeId);
        Assert.Equal("$result", definition.Call.Result.ProjectionKey);
        Assert.Equal("length", definition.Length!.ProjectionKey);
        Assert.Equal("Approved", definition.Approved!.OutcomeKey);
    }

    [Fact]
    public void Compiled_authored_state_contains_no_builder_carriers_or_callbacks()
    {
        var state = new ArgumentWorkflow().Compile();

        var json = JsonSerializer.Serialize(state);

        Assert.DoesNotContain(nameof(ActivityArgument), json, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowValue", json, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Action", json, StringComparison.Ordinal);
        Assert.Contains("WorkflowRequest", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_arguments_round_trip_authored_conversion_controls()
    {
        var state = new ConversionWorkflow().Compile();
        var node = Assert.Single(Children(state));
        var input = Assert.Single(node.Inputs);

        Assert.Equal(AuthoredValueConversionMode.Json, input.Conversion!.Mode);
        Assert.Equal(32, input.Conversion.Limits!.MaxDepth);

        var roundTripped = JsonSerializer.Deserialize<WorkflowDefinitionState>(JsonSerializer.Serialize(state))!;
        var restored = Assert.Single(Assert.Single(Children(roundTripped)).Inputs);
        Assert.Equal(AuthoredValueConversionMode.Json, restored.Conversion!.Mode);
        Assert.Equal(32, restored.Conversion.Limits!.MaxDepth);
    }

    [Fact]
    public void Structured_conditionals_lower_lexical_regions_and_derive_connections_deterministically()
    {
        var definition = new StructuredWorkflow();

        var state = definition.Compile();

        var root = Children(state).ToArray();
        Assert.Equal(["before", "decision", "after"], root.Select(node => node.NodeId));

        var decision = root[1];
        Assert.Equal("Condition", Assert.Single(decision.Inputs).ReferenceKey);
        var conditional = decision.Structure!.Payload.Deserialize<IfAuthoredStructure>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("decision-then", conditional.Then!.NodeId);
        Assert.Equal("decision-else", conditional.Else!.NodeId);

        var thenChildren = Children(conditional.Then).ToArray();
        Assert.Equal(["then-first", "then-second", "intrinsic-5"], thenChildren.Select(node => node.NodeId));
        Assert.Equal(definition.ThenVariable!.Value.DeclaringScopeId, definition.ThenFirst!.Node.ScopeId);
        Assert.Equal(definition.ThenFirst.Node.ScopeId, definition.ThenSecond!.Node.ScopeId);
        var priorResult = Assert.Single(thenChildren[1].Inputs);
        var priorResultBinding = Assert.IsType<JsonElement>(priorResult.Value.Value);
        Assert.Equal("then-first", priorResultBinding.GetProperty("producerNodeId").GetString());
        Assert.Equal(definition.ThenFirst.Node.ScopeId, priorResultBinding.GetProperty("producerScopeId").GetString());
        Assert.Single(conditional.Then.Structure!.Payload.GetProperty("variables").EnumerateArray());
        Assert.Empty(state.Variables);
        Assert.Equal(JsonSerializer.Serialize(state), JsonSerializer.Serialize(new StructuredWorkflow().Compile()));
    }

    [Theory]
    [InlineData(false, "VF-AUTH-003")]
    [InlineData(true, "VF-AUTH-004")]
    public void Lexical_values_cannot_escape_to_a_sibling_or_parent_scope(bool resultSource, string diagnostic)
    {
        var definition = new InvalidLexicalWorkflow(resultSource);

        var exception = Assert.Throws<InvalidOperationException>(() => definition.Compile());

        Assert.Contains(diagnostic, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_child_calls_and_ordinary_extensions_lower_directly_to_authored_nodes()
    {
        var definition = new ReuseWorkflow();

        var state = definition.Compile();
        var children = Children(state).ToArray();

        Assert.Equal(["extension-first", "extension-second", "child"], children.Select(node => node.NodeId));
        Assert.Equal("$result", definition.ChildCall!.Result.ProjectionKey);
        var request = Assert.Single(children[2].Inputs);
        Assert.Equal("request", request.ReferenceKey);
        Assert.Equal("WorkflowRequest", request.Value.ExpressionType);

        var json = JsonSerializer.Serialize(state);
        Assert.DoesNotContain(nameof(ChildWorkflowCall<object>), json, StringComparison.Ordinal);
        Assert.DoesNotContain("Extension", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Variables_and_value_operations_lower_to_typed_graph_visible_intrinsics()
    {
        var state = new IntrinsicWorkflow().Compile();
        var children = Children(state).ToArray();

        Assert.Equal(5, children.Length);
        Assert.Equal(
            [
                AuthoredWorkflowIntrinsicKind.Set,
                AuthoredWorkflowIntrinsicKind.Merge,
                AuthoredWorkflowIntrinsicKind.Reduce,
                AuthoredWorkflowIntrinsicKind.Control,
                AuthoredWorkflowIntrinsicKind.Return
            ],
            children.Select(node => node.Intrinsic!.Kind));

        var variableWrites = children.Take(3).ToArray();
        Assert.All(variableWrites, node =>
        {
            Assert.Equal("items", node.Intrinsic!.Variable!.ReferenceKey);
            Assert.Equal(VariableReference.WorkflowScopeId, node.Intrinsic.Variable.DeclaringScopeId);
            Assert.Equal("String", node.Intrinsic.ValueType!.Alias);
            Assert.Equal(CollectionKind.List, node.Intrinsic.ValueType.CollectionKind);
            Assert.Equal("value", Assert.Single(node.Inputs).ReferenceKey);
        });

        var outcome = Assert.IsType<JsonElement>(Assert.Single(children[3].Inputs).Value.Value);
        Assert.Equal("Approved", outcome.GetString());
        Assert.Equal("String", children[4].Intrinsic!.ValueType!.Alias);
        Assert.Equal("value", Assert.Single(children[4].Inputs).ReferenceKey);
        var json = JsonSerializer.Serialize(state);
        Assert.DoesNotContain("ActivityArgument", json, StringComparison.Ordinal);
        var roundTripped = JsonSerializer.Deserialize<WorkflowDefinitionState>(json)!;
        Assert.Equal(
            children.Select(node => node.Intrinsic!.Kind),
            Children(roundTripped).Select(node => node.Intrinsic!.Kind));
    }

    [Fact]
    public void Workflow_effects_lower_to_graph_visible_intrinsics_with_static_targets()
    {
        var children = Children(new WorkflowEffectsWorkflow().Compile()).ToArray();

        Assert.Equal(
            [
                AuthoredWorkflowIntrinsicKind.SetCorrelationId,
                AuthoredWorkflowIntrinsicKind.SetInstanceName,
                AuthoredWorkflowIntrinsicKind.SetOutput,
                AuthoredWorkflowIntrinsicKind.Finish
            ],
            children.Select(node => node.Intrinsic!.Kind));
        Assert.Equal("value", Assert.Single(children[0].Inputs).ReferenceKey);
        Assert.Equal(["name", "value"], children[2].Inputs.Select(input => input.ReferenceKey).Order(StringComparer.Ordinal));
        var outputName = Assert.IsType<JsonElement>(children[2].Inputs.Single(input => input.ReferenceKey == "name").Value.Value);
        Assert.Equal("result", outputName.GetString());
        var outcome = Assert.IsType<JsonElement>(Assert.Single(children[3].Inputs).Value.Value);
        Assert.Equal("Aborted", outcome.GetString());
        Assert.All(children, child => Assert.StartsWith("elsa.intrinsic.", child.ActivityVersionId, StringComparison.Ordinal));
    }

    private static IReadOnlyCollection<ActivityNode> Children(WorkflowDefinitionState state) =>
        state.RootActivity!.Structure!.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static IReadOnlyCollection<ActivityNode> Children(ActivityNode node) =>
        node.Structure!.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private sealed record Request(string Message);
    private sealed record Result(string Message);
    private sealed class Write;
    private sealed class Probe;

    private sealed class ConversionWorkflow : WorkflowDefinition<Request, Result>
    {
        protected override void Build(IWorkflowBuilder<Request, Result> workflow) =>
            workflow.Sequence.Add<Write, string>("test/write@1", inputs => inputs.Set(
                "payload",
                ActivityArgument.Value("""{"message":"hello"}""").WithConversion(new AuthoredValueConversionRequest(
                    AuthoredValueConversionMode.Json,
                    Limits: new AuthoredValueConversionLimits(MaxDepth: 32)))));
    }

    private sealed class TestWorkflow : WorkflowDefinition<Request, Result>
    {
        public int BuildCount { get; private set; }

        protected override void Build(IWorkflowBuilder<Request, Result> workflow)
        {
            BuildCount++;
            workflow.Sequence.Add<Write, string>("test/write@1", nodeId: "write-1");
            workflow.Sequence.Add<Write, string>("test/write@1", nodeId: "write-2");
            workflow.Return(workflow.Value(new Result("done")));
        }
    }

    private sealed class ArgumentWorkflow : WorkflowDefinition<Request, Result>
    {
        protected override void Build(IWorkflowBuilder<Request, Result> workflow)
        {
            var source = workflow.From(request => request.Message);
            workflow.Sequence.Add<Write, string>("test/write@1", inputs => inputs
                .From("source", source)
                .Set("literal", ActivityArgument.Value("hello"))
                .Set("null", ActivityArgument.Null<string>())
                .Set("default", ActivityArgument.Default<string>())
                .Set<string>("omitted", default), nodeId: "write");
        }
    }

    private sealed class HandleWorkflow : WorkflowDefinition<Request, Result>
    {
        public ActivityCall<string>? Call { get; private set; }
        public ActivityResultSource<int>? Length { get; private set; }
        public ActivityOutcomeSource? Approved { get; private set; }

        protected override void Build(IWorkflowBuilder<Request, Result> workflow)
        {
            Call = workflow.Sequence.Add<Write, string>("test/write@1", nodeId: "probe");
            Length = Call.Output<int>("length");
            Approved = Call.Outcome("Approved");
        }
    }

    private sealed record StructuredRequest(bool Enabled);

    private sealed class StructuredWorkflow : WorkflowDefinition<StructuredRequest, string>
    {
        public ActivityCall<string>? Before { get; private set; }
        public ActivityCall<object?>? Decision { get; private set; }
        public ActivityCall<string>? After { get; private set; }
        public Variable<int>? ThenVariable { get; private set; }
        public ActivityCall<string>? ThenFirst { get; private set; }
        public ActivityCall<string>? ThenSecond { get; private set; }

        protected override void Build(IWorkflowBuilder<StructuredRequest, string> workflow)
        {
            Before = workflow.Sequence.Add<Probe, string>("test/probe@1", nodeId: "before");
            Decision = workflow.If(
                workflow.From(request => request.Enabled),
                then: branch =>
                {
                    ThenVariable = branch.Variable<int>("attempts", 1);
                    ThenFirst = branch.Add<Probe, string>("test/probe@1", nodeId: "then-first");
                    ThenSecond = branch.Add<Probe, string>(
                        "test/probe@1",
                        inputs => inputs.From("prior", ThenFirst.Result),
                        "then-second");
                    branch.Set(ThenVariable, workflow.Value(2));
                },
                @else: branch => branch.Add<Probe, string>("test/probe@1", nodeId: "else-only"),
                nodeId: "decision");
            After = workflow.Sequence.Add<Probe, string>("test/probe@1", nodeId: "after");
        }
    }

    private sealed class InvalidLexicalWorkflow(bool resultSource) : WorkflowDefinition<StructuredRequest, string>
    {
        protected override void Build(IWorkflowBuilder<StructuredRequest, string> workflow)
        {
            Variable<int>? branchVariable = null;
            ActivityCall<string>? branchResult = null;
            workflow.If(
                workflow.From(request => request.Enabled),
                then: branch =>
                {
                    branchVariable = branch.Variable<int>("branch-value", 1);
                    branchResult = branch.Add<Probe, string>("test/probe@1", nodeId: "branch-result");
                },
                @else: resultSource
                    ? null
                    : branch => branch.Add<Probe, string>(
                        "test/probe@1",
                        inputs => inputs.From("invalid", branchVariable!.Value),
                        "invalid-sibling"),
                nodeId: "decision");

            if (resultSource)
            {
                workflow.Sequence.Add<Probe, string>(
                    "test/probe@1",
                    inputs => inputs.From("invalid", branchResult!.Result),
                    "invalid-parent");
            }
        }
    }

    private sealed record ChildRequest(string Message);
    private sealed record ChildResult(string Message);
    private sealed record ReuseRequest(ChildRequest Child);

    private sealed class ChildWorkflow : WorkflowDefinition<ChildRequest, ChildResult>
    {
        protected override void Build(IWorkflowBuilder<ChildRequest, ChildResult> workflow) =>
            workflow.Return(workflow.Value(new ChildResult("done")));
    }

    private sealed class ReuseWorkflow : WorkflowDefinition<ReuseRequest, string>
    {
        public ActivityCall<string>? ExtensionCall { get; private set; }
        public ChildWorkflowCall<ChildResult>? ChildCall { get; private set; }

        protected override void Build(IWorkflowBuilder<ReuseRequest, string> workflow)
        {
            ExtensionCall = workflow.AddPair();
            ChildCall = workflow.Invoke<ChildWorkflow, ChildRequest, ChildResult>(
                "test/child-workflow@1",
                workflow.From(request => request.Child),
                "child");
        }
    }

    private sealed class IntrinsicWorkflow : WorkflowDefinition<Request, string>
    {
        protected override void Build(IWorkflowBuilder<Request, string> workflow)
        {
            var items = workflow.Variable<IReadOnlyList<string>>("items", []);
            workflow.Set(items, workflow.Value<IReadOnlyList<string>>(["set"]));
            workflow.Merge(items, workflow.Value<IReadOnlyList<string>>(["merged"]));
            workflow.Reduce(items, workflow.Value<IReadOnlyList<string>>(["reduced"]));
            workflow.Control("Approved");
            workflow.Return(workflow.Value("done"));
        }
    }

    private sealed class WorkflowEffectsWorkflow : WorkflowDefinition<Request, string>
    {
        protected override void Build(IWorkflowBuilder<Request, string> workflow)
        {
            workflow.SetCorrelationId(workflow.Value("correlation"));
            workflow.SetInstanceName(workflow.Value("instance"));
            workflow.SetOutput("result", workflow.Value("accepted"));
            workflow.Finish("Aborted");
        }
    }
}

internal static class WorkflowBuilderTestExtensions
{
    public static ActivityCall<string> AddPair(this ISequenceBuilder sequence)
    {
        sequence.Add<object, string>("test/probe@1", nodeId: "extension-first");
        return sequence.Add<object, string>("test/probe@1", nodeId: "extension-second");
    }
}
