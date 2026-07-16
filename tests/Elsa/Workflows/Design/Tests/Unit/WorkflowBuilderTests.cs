using System.Text.Json;
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

    private static IReadOnlyCollection<ActivityNode> Children(WorkflowDefinitionState state) =>
        state.RootActivity!.Structure!.Payload.GetProperty("activities").Deserialize<ActivityNode[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private sealed record Request(string Message);
    private sealed record Result(string Message);
    private sealed class Write;

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
}
