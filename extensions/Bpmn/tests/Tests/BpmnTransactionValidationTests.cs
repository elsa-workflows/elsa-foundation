using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// Validation coverage for transactions: the transaction placement rules (subprocess-only, bound child,
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
                BpmnTestNodes.StartEvent(),
                new BpmnElement("t", BpmnElementTypes.Task, childNodeId: "node-t", isTransaction: true),
                BpmnTestNodes.EndEvent()
            ],
            flows: [BpmnTestNodes.Flow("f1", "start", "t"), BpmnTestNodes.Flow("f2", "t", "end")],
            children: [Probe("node-t")]);

        var exception = Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(node));
        Assert.True(exception.Message.Contains("transaction", StringComparison.OrdinalIgnoreCase), exception.ToString());
    }

    [Fact]
    public void IsTransaction_WithLoopCharacteristics_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnTestNodes.StartEvent(),
                new BpmnElement("sub", BpmnElementTypes.SubProcess, childNodeId: "node-sub", isTransaction: true,
                    loopCharacteristics: new BpmnLoopCharacteristics(isSequential: true, cardinality: 2)),
                BpmnTestNodes.EndEvent()
            ],
            flows: [BpmnTestNodes.Flow("f1", "start", "sub"), BpmnTestNodes.Flow("f2", "sub", "end")],
            children: [Probe("node-sub")]);

        var exception = Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(node));
        Assert.True(exception.Message.Contains("multi-instance", StringComparison.OrdinalIgnoreCase), exception.ToString());
    }

    [Fact]
    public void CancelEnd_OutsideTransactionStructure_IsRejected()
    {
        var node = NewExecutableNode(
            elements: [BpmnTestNodes.StartEvent(), BpmnTestNodes.CancelEnd("cancel-end")],
            flows: [BpmnTestNodes.Flow("f1", "start", "cancel-end")],
            isTransaction: false);

        var exception = Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(node));
        Assert.True(exception.Message.Contains("cancel end event", StringComparison.OrdinalIgnoreCase), exception.ToString());
    }

    [Fact]
    public void CancelBoundary_OnNonTransactionHost_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnTestNodes.StartEvent(),
                new BpmnElement("sub", BpmnElementTypes.SubProcess, childNodeId: "node-sub"),
                BpmnTestNodes.CancelBoundary("cb", "sub"),
                BpmnTestNodes.EndEvent(),
                BpmnTestNodes.EndEvent("cancelled-end")
            ],
            flows:
            [
                BpmnTestNodes.Flow("f1", "start", "sub"),
                BpmnTestNodes.Flow("f2", "sub", "end"),
                BpmnTestNodes.Flow("f3", "cb", "cancelled-end")
            ],
            children: [Probe("node-sub")]);

        var exception = Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(node));
        Assert.True(exception.Message.Contains("not a transaction", StringComparison.OrdinalIgnoreCase), exception.ToString());
    }

    [Fact]
    public void TwoCancelBoundaries_OnOneTransactionHost_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnTestNodes.StartEvent(),
                BpmnTestNodes.TransactionSubProcess("txn", "node-txn"),
                BpmnTestNodes.CancelBoundary("cb1", "txn"),
                BpmnTestNodes.CancelBoundary("cb2", "txn"),
                BpmnTestNodes.EndEvent(),
                BpmnTestNodes.EndEvent("c1"),
                BpmnTestNodes.EndEvent("c2")
            ],
            flows:
            [
                BpmnTestNodes.Flow("f1", "start", "txn"),
                BpmnTestNodes.Flow("f2", "txn", "end"),
                BpmnTestNodes.Flow("f3", "cb1", "c1"),
                BpmnTestNodes.Flow("f4", "cb2", "c2")
            ],
            children: [Probe("node-txn")]);

        var exception = Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(node));
        Assert.True(exception.Message.Contains("at most one", StringComparison.OrdinalIgnoreCase), exception.ToString());
    }

    [Fact]
    public void CancelBoundary_WithoutOutboundFlow_IsRejected()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnTestNodes.StartEvent(),
                BpmnTestNodes.TransactionSubProcess("txn", "node-txn"),
                BpmnTestNodes.CancelBoundary("cb", "txn"),
                BpmnTestNodes.EndEvent()
            ],
            flows: [BpmnTestNodes.Flow("f1", "start", "txn"), BpmnTestNodes.Flow("f2", "txn", "end")],
            children: [Probe("node-txn")]);

        // A cancel boundary is not exempt from the ≥1-outbound rule (unlike a compensation boundary).
        var exception = Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(node));
        Assert.True(exception.Message.Contains("outbound", StringComparison.OrdinalIgnoreCase), exception.ToString());
    }

    [Fact]
    public void ValidNestedTransaction_WithCancelEnd_Validates()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnTestNodes.StartEvent(),
                BpmnTestNodes.Task("task", childNodeId: "node-task"),
                BpmnTestNodes.CancelEnd("cancel-end")
            ],
            flows: [BpmnTestNodes.Flow("f1", "start", "task"), BpmnTestNodes.Flow("f2", "task", "cancel-end")],
            children: [Probe("node-task")],
            isTransaction: true);

        var graph = BpmnGraphAdapter.Build(node);

        Assert.True(graph.IsTransaction);
    }

    [Fact]
    public void ValidParentTransaction_WithCancelBoundary_Validates()
    {
        var node = NewExecutableNode(
            elements:
            [
                BpmnTestNodes.StartEvent(),
                BpmnTestNodes.TransactionSubProcess("txn", "node-txn"),
                BpmnTestNodes.CancelBoundary("cb", "txn"),
                BpmnTestNodes.EndEvent(),
                BpmnTestNodes.EndEvent("cancelled-end")
            ],
            flows:
            [
                BpmnTestNodes.Flow("f1", "start", "txn"),
                BpmnTestNodes.Flow("f2", "txn", "end"),
                BpmnTestNodes.Flow("f3", "cb", "cancelled-end")
            ],
            children: [Probe("node-txn")]);

        var graph = BpmnGraphAdapter.Build(node);

        Assert.NotNull(graph.AttachedCancelBoundary("txn"));
        Assert.Empty(graph.AttachedCatchBoundaries("txn"));
    }

    private static ExecutableNode Probe(string nodeId) =>
        BpmnTestNodes.Probe(nodeId);

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
