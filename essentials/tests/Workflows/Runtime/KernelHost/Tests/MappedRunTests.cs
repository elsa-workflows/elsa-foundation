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
        // A publish lands between any two reads: a second read would see version 5.
        var reads = 0;
        var served = new Native.NativeExecutionOptions
        {
            ServedWorkflow = id => id == "invoicing" ? Native.ServedDefinition.By(++reads == 1 ? "invoicing-artifact-4" : "invoicing-artifact-5") : Native.ServedDefinition.None,
        };

        var resolved = await NativeTurn.StartAsync(workflow, served);
        var unresolved = await NativeTurn.StartAsync(workflow);

        // Read once, and that answer checked and started the child.
        Assert.Equal(1, reads);
        var (start, detached) = Assert.Single(resolved.Children);
        Assert.Equal(("invoicing-artifact-4", !wait), (start.Workflow, detached));
        // A host that serves no publications starts the id itself.
        Assert.Equal("invoicing", Assert.Single(unresolved.Children).Start.Workflow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dispatch_of_a_definition_the_host_does_not_publish_faults_before_a_child_starts(bool wait)
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "invoicing", "String"), Literal("WaitForCompletion", wait, "Boolean")])));
        var resolves = new List<string>();
        var publishing = new Native.NativeExecutionOptions
        {
            ServedWorkflow = _ => Native.ServedDefinition.None,
            // A stored artifact of that id exists: the Dispatch still does not reach it.
            ResolveWorkflow = (id, _) =>
            {
                resolves.Add(id);
                return ValueTask.FromResult<Native.Workflow?>(NativeExecutableMapper.Map(ExecutableOf(id, id, WriteLine("w", Literal("text", "stored")))));
            },
        };

        var unknown = await NativeTurn.StartAsync(workflow, publishing);

        Assert.Empty(unknown.Children);
        Assert.Empty(resolves);
        Assert.Equal(Contract.ActorStatus.Faulted, unknown.Status);
        Assert.Equal(("dispatch.unpublished", "Dispatch Workflow names definition 'invoicing', which is not published on this host. Publish it, then run the workflow again."),
            (unknown.Output?["Code"]?.GetValue<string>(), unknown.Output?["Message"]?.GetValue<string>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dispatch_of_a_definition_several_channels_serve_without_a_default_faults(bool wait)
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "invoicing", "String"), Literal("WaitForCompletion", wait, "Boolean")])));

        var turn = await NativeTurn.StartAsync(workflow, new Native.NativeExecutionOptions { ServedWorkflow = _ => Native.ServedDefinition.SeveralServe });

        Assert.Empty(turn.Children);
        Assert.Equal(Contract.ActorStatus.Faulted, turn.Status);
        Assert.Equal(("dispatch.ambiguous", "Definition 'invoicing' is published in several channels and none is 'default'. Publish it to the default channel."),
            (turn.Output?["Code"]?.GetValue<string>(), turn.Output?["Message"]?.GetValue<string>()));
    }

    [Fact]
    public async Task A_dispatch_of_a_workflow_the_registry_holds_starts_it_on_a_publishing_host()
    {
        var workflow = NativeExecutableMapper.Map(Executable(Node("dispatch", Types.Dispatch,
            [Literal("WorkflowDefinitionId", "helper", "String"), Literal("WaitForCompletion", true, "Boolean")])));
        var helper = NativeExecutableMapper.Map(ExecutableOf("helper", "helper", WriteLine("w", Literal("text", "help"))));
        var model = new Native.NativeExecutionModel(new Native.WorkflowRegistry().Add(workflow).Add(helper),
            new Native.NativeExecutionOptions { ServedWorkflow = _ => Native.ServedDefinition.None });

        var turn = await NativeTurn.StartAsync(model, workflow);

        Assert.Equal("helper", Assert.Single(turn.Children).Start.Workflow);
    }
}
