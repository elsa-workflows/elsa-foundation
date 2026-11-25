using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>Mapped workflows run on the Native model, and a Dispatch starts the artifact the host serves for its definition.</summary>
public sealed class MappedRunTests
{
    [Fact]
    public async Task A_mapped_workflow_runs_to_its_end_on_the_native_model()
    {
        var root = Sequence("root",
            Set("set", "v-x", RequestMember("value", "Order", "amount")),
            Intrinsic("correlate", WorkflowIntrinsicKind.SetCorrelationId, null, RequestMember(WorkflowIntrinsicInputKeys.Value, "Order", "id")),
            Node("check", Types.If, [Literal("Condition", true, "Boolean")], Structure("elsa.if.structure", new { then = "yes", @else = "no" }),
                [Slot("If.Then", SetOutput("yes", "total", VariableRead("value", "v-x"))), Slot("If.Else", SetOutput("no", "total", Literal("value", 0)))]),
            StructureTestsGraph(),
            SetOutput("approved", "approved", VariableRead("value", "v-out")),
            Intrinsic("finish", WorkflowIntrinsicKind.Finish, null, Literal(WorkflowIntrinsicInputKeys.Outcome, "Approved", "String")));
        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-x", "x"), Variable("v-out", "out")));

        var turn = await NativeTurn.StartAsync(workflow, input: new JsonObject { ["Order"] = new JsonObject { ["id"] = "order-42", ["amount"] = 12 } });

        Assert.Equal(Contract.ActorStatus.Finished, turn.Status);
        Assert.Equal(12, turn.Output!["total"]!.GetValue<int>());
        // The GraphActivity's body set its variable from its input and assigned its output, which the capture read.
        Assert.Equal(7, turn.Output["approved"]!.GetValue<int>());
        Assert.Equal("order-42", turn.Labels[Native.NativeLabels.CorrelationId]);
    }

    [Fact]
    public async Task A_loop_body_reads_its_item_and_index_on_the_native_model()
    {
        var root = Sequence("root",
            Node("each", Types.ForEach, [Literal("Collection", new[] { "a", "b", "c" })], Structure("elsa.foreach.structure", new { body = "item-body" }),
                [Slot("ForEach.Body", Sequence("item-body",
                    Set("item", "v-item", VariableRead("value", "currentItem", scope: "each")),
                    Set("position", "v-position", VariableRead("value", "currentIndex", scope: "each"))))]),
            Node("count", Types.For, [Literal("Start", 0, "Int32"), Literal("End", 4, "Int32")], Structure("elsa.for.structure", new { body = "pass" }),
                [Slot("For.Body", Set("pass", "v-index", VariableRead("value", "index", scope: "count")))]),
            SetOutput("item-out", "item", VariableRead("value", "v-item")),
            SetOutput("position-out", "position", VariableRead("value", "v-position")),
            SetOutput("index-out", "index", VariableRead("value", "v-index")));
        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-item", "item"), Variable("v-position", "position"), Variable("v-index", "lastIndex")));

        var turn = await NativeTurn.StartAsync(workflow);

        Assert.Equal(Contract.ActorStatus.Finished, turn.Status);
        Assert.Equal("c", turn.Output!["item"]!.GetValue<string>());
        Assert.Equal(2, turn.Output["position"]!.GetValue<int>());
        Assert.Equal(3, turn.Output["index"]!.GetValue<int>());
    }

    private static ExecutableNode StructureTestsGraph() => MappingStructureTests.Graph("approve", out _);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dispatch_starts_the_artifact_the_host_serves_for_its_definition(bool wait)
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "invoicing", "String"), Literal("WaitForCompletion", wait, "Boolean")])));
        var served = new Native.NativeExecutionOptions { ServedWorkflow = id => id == "invoicing" ? "invoicing-artifact-4" : null };

        var resolved = await NativeTurn.StartAsync(workflow, served);
        var unresolved = await NativeTurn.StartAsync(workflow);
        var unknown = await NativeTurn.StartAsync(workflow, new Native.NativeExecutionOptions { ServedWorkflow = _ => null });

        var (start, detached) = Assert.Single(resolved.Children);
        Assert.Equal(("invoicing-artifact-4", !wait), (start.Workflow, detached));
        Assert.Equal("invoicing", Assert.Single(unresolved.Children).Start.Workflow);
        Assert.Equal("invoicing", Assert.Single(unknown.Children).Start.Workflow);
    }
}
