using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// Validation coverage for spec 125 transactions: the transaction placement rules (subprocess-only, bound child,
/// no loop characteristics), the cancel-end placement rule (only inside a transaction), and the cancel-boundary
/// rules (transaction host only, at most one per host). A valid transaction — nested (cancel end) and parent
/// (transaction element + cancel boundary) — validates cleanly.
/// </summary>
public sealed class BpmnTransactionValidationTests
{
    [Fact]
    public void IsTransaction_OnNonSubProcess_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("t", BpmnElementTypes.Task, childNodeId: "node-t", isTransaction: true),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows: [BpmnRuntimeFixture.Flow("f1", "start", "t"), BpmnRuntimeFixture.Flow("f2", "t", "end")],
            children: [Probe("node-t")]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("transaction", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsTransaction_WithLoopCharacteristics_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("sub", BpmnElementTypes.SubProcess, childNodeId: "node-sub", isTransaction: true,
                    loopCharacteristics: new BpmnLoopCharacteristics(isSequential: true, cardinality: 2)),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows: [BpmnRuntimeFixture.Flow("f1", "start", "sub"), BpmnRuntimeFixture.Flow("f2", "sub", "end")],
            children: [Probe("node-sub")]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("multi-instance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancelEnd_OutsideTransactionStructure_IsRejected()
    {
        var node = NewExecutableNode(
            elements: [BpmnRuntimeFixture.StartEvent(), BpmnRuntimeFixture.CancelEnd("cancel-end")],
            flows: [BpmnRuntimeFixture.Flow("f1", "start", "cancel-end")],
            isTransaction: false);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("cancel end event", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancelBoundary_OnNonTransactionHost_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("sub", BpmnElementTypes.SubProcess, childNodeId: "node-sub"),
                BpmnRuntimeFixture.CancelBoundary("cb", "sub"),
                BpmnRuntimeFixture.EndEvent(),
                BpmnRuntimeFixture.EndEvent("cancelled-end")
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("f1", "start", "sub"),
                BpmnRuntimeFixture.Flow("f2", "sub", "end"),
                BpmnRuntimeFixture.Flow("f3", "cb", "cancelled-end")
            ],
            children: [Probe("node-sub")]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("not a transaction", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoCancelBoundaries_OnOneTransactionHost_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.TransactionSubProcess("txn", "node-txn"),
                BpmnRuntimeFixture.CancelBoundary("cb1", "txn"),
                BpmnRuntimeFixture.CancelBoundary("cb2", "txn"),
                BpmnRuntimeFixture.EndEvent(),
                BpmnRuntimeFixture.EndEvent("c1"),
                BpmnRuntimeFixture.EndEvent("c2")
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("f1", "start", "txn"),
                BpmnRuntimeFixture.Flow("f2", "txn", "end"),
                BpmnRuntimeFixture.Flow("f3", "cb1", "c1"),
                BpmnRuntimeFixture.Flow("f4", "cb2", "c2")
            ],
            children: [Probe("node-txn")]);

        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("at most one", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancelBoundary_WithoutOutboundFlow_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.TransactionSubProcess("txn", "node-txn"),
                BpmnRuntimeFixture.CancelBoundary("cb", "txn"),
                BpmnRuntimeFixture.EndEvent()
            ],
            flows: [BpmnRuntimeFixture.Flow("f1", "start", "txn"), BpmnRuntimeFixture.Flow("f2", "txn", "end")],
            children: [Probe("node-txn")]);

        // A cancel boundary is not exempt from the ≥1-outbound rule (unlike a compensation boundary).
        var exception = Assert.Throws<BpmnExecutionException>(() => BpmnGraph.From(node));
        Assert.Contains("outbound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidNestedTransaction_WithCancelEnd_Validates()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("task", childNodeId: "node-task"),
                BpmnRuntimeFixture.CancelEnd("cancel-end")
            ],
            flows: [BpmnRuntimeFixture.Flow("f1", "start", "task"), BpmnRuntimeFixture.Flow("f2", "task", "cancel-end")],
            children: [Probe("node-task")],
            isTransaction: true);

        var graph = BpmnGraph.From(node);

        Assert.True(graph.IsTransaction);
    }

    [Fact]
    public void ValidParentTransaction_WithCancelBoundary_Validates()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.TransactionSubProcess("txn", "node-txn"),
                BpmnRuntimeFixture.CancelBoundary("cb", "txn"),
                BpmnRuntimeFixture.EndEvent(),
                BpmnRuntimeFixture.EndEvent("cancelled-end")
            ],
            flows:
            [
                BpmnRuntimeFixture.Flow("f1", "start", "txn"),
                BpmnRuntimeFixture.Flow("f2", "txn", "end"),
                BpmnRuntimeFixture.Flow("f3", "cb", "cancelled-end")
            ],
            children: [Probe("node-txn")]);

        var graph = BpmnGraph.From(node);

        Assert.NotNull(graph.AttachedCancelBoundary("txn"));
        Assert.Empty(graph.AttachedCatchBoundaries("txn"));
    }

    private static ExecutableNode Probe(string nodeId) =>
        WorkflowExecutionHarness.NewProbeNode(nodeId);

    private static ExecutableNode NewExecutableNode(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        IReadOnlyCollection<ExecutableNode>? children = null,
        bool isTransaction = false) =>
        new(
            executableNodeId: "node-bpmn",
            authoredActivityId: "authored-bpmn",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "BpmnDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, children ?? [])],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows, isTransaction: isTransaction))));
}
