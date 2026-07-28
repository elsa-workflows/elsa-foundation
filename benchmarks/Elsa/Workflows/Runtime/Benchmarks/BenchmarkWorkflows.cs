using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Workflows.Runtime.Benchmarks;

/// <summary>
/// Shared graph builders for the engine benchmarks: the canonical 2-node (Flowchart root + single leaf) and the
/// hot-loop (Flowchart root + straight-line chain of leaves) executables. Extracted so the single-run A/B suite
/// (<see cref="EngineExecutionBenchmarks"/>) and the concurrency instrument (<see cref="EngineConcurrencyBenchmarks"/>)
/// build byte-identical graphs from one place rather than each re-deriving the node/connection wiring.
///
/// Every builder takes an optional <see cref="WorkflowExecutableIdentity"/>: single-run suites leave it null (the
/// harness default identity, <c>artifact-1</c>), while the concurrency instrument stamps a DISTINCT identity per
/// execution so N runs can share one durable store without colliding on the executable-store artifact key.
/// </summary>
internal static class BenchmarkWorkflows
{
    public const string WriteLineNodeId = "node-writeline";
    public const string FlowchartNodeId = "node-flowchart";

    public static string LoopNodeId(int index) => $"node-loop-{index}";

    /// <summary>The 2-node executable: a Flowchart root whose single start node is a WriteLine leaf.</summary>
    public static WorkflowExecutable TwoNode(WorkflowExecutableIdentity? identity = null) =>
        Flowchart([NewWriteLineNode(WriteLineNodeId, "Hello from the Elsa engine benchmark.")], connections: [], startNodeId: WriteLineNodeId, identity);

    /// <summary>
    /// The hot-loop executable: a Flowchart whose start node begins a straight-line chain of <paramref name="length"/>
    /// leaf activities (leaf shape chosen by <paramref name="makeLeaf"/>), each wired to the next by a default-outcome
    /// connection.
    /// </summary>
    public static WorkflowExecutable HotLoop(int length, Func<int, ExecutableNode> makeLeaf, WorkflowExecutableIdentity? identity = null)
    {
        var leaves = Enumerable.Range(0, length).Select(makeLeaf).ToArray();
        var connections = Enumerable.Range(0, length - 1)
            .Select(index => new FlowchartConnection(
                new FlowchartEndpoint(leaves[index].ExecutableNodeId),
                new FlowchartEndpoint(leaves[index + 1].ExecutableNodeId)))
            .ToArray();
        return Flowchart(leaves, connections, startNodeId: leaves[0].ExecutableNodeId, identity);
    }

    /// <summary>A pure benchmark-local leaf (<see cref="NoOpStep"/>, ReplaySafe) for the hot-loop body: no external effect.</summary>
    public static ExecutableNode NoOpLeaf(int index) =>
        new(
            executableNodeId: LoopNodeId(index),
            authoredActivityId: $"authored-{LoopNodeId(index)}",
            activityType: typeof(NoOpStep).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    /// <summary>
    /// A real <see cref="WriteLine"/> CLR leaf with a literal <c>text</c> input. The activity contract is left unset so
    /// the harness reflects it from the CLR type (its established path for non-probe CLR nodes).
    /// </summary>
    public static ExecutableNode NewWriteLineNode(string nodeId, string text)
    {
        var stringType = new ValueTypeDescriptor("String");
        var binding = new RuntimeInputBinding(
            inputKey: "text",
            targetType: stringType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                stringType,
                JsonSerializer.SerializeToElement(text),
                ValueProtectionPolicy.InstanceInline));

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(WriteLine).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["text"] = binding },
            metadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutable Flowchart(
        IReadOnlyCollection<ExecutableNode> leaves,
        IReadOnlyCollection<FlowchartConnection> connections,
        string startNodeId,
        WorkflowExecutableIdentity? identity)
    {
        var root = new ExecutableNode(
            executableNodeId: FlowchartNodeId,
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "elsa.flowchart",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, leaves)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new FlowchartStructure(connections: connections, startNodeId: startNodeId))));

        return identity is { } id
            ? WorkflowExecutionHarness.NewExecutable(root, id)
            : WorkflowExecutionHarness.NewExecutable(root);
    }
}
