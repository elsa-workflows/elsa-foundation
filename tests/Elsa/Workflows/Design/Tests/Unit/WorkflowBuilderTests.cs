using System.Text.Json;
using Elsa.Activities.If.Authoring;
using Elsa.Activities.If.Models;
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

    private static IReadOnlyCollection<ActivityNode> Children(WorkflowDefinitionState state) =>
        state.RootActivity!.Structure!.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static IReadOnlyCollection<ActivityNode> Children(ActivityNode node) =>
        node.Structure!.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private sealed record Request(string Message);
    private sealed record Result(string Message);
    private sealed class Write;
    private sealed class Probe;

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
}

internal static class WorkflowBuilderTestExtensions
{
    public static ActivityCall<string> AddPair(this ISequenceBuilder sequence)
    {
        sequence.Add<object, string>("test/probe@1", nodeId: "extension-first");
        return sequence.Add<object, string>("test/probe@1", nodeId: "extension-second");
    }
}
