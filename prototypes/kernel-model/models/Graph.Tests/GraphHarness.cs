using System.Text.Json;
using Kernel.Graph.Contracts;
using Kernel.Graph.Exceptions;
using Kernel.Graph.Internal;
using Kernel.Graph.Models;

namespace Kernel.Graph.Tests;

/// <summary>
/// The host of the graph engine in these tests: what main's <c>WorkflowExecutionHarness</c> does for a Flowchart, and
/// no more. One container with leaf children (probes with a fixed outcome, or leaves that fault); scheduled children
/// run one at a time in the order they were scheduled (first in, first out, as main's harness runs them); each run
/// takes the next execution id from a pool, the container first; the engine's state is carried from one callback to
/// the next exactly as main stages it.
/// <para>
/// What it observes is what main's corpus observes: the order children ran in, the container's terminal status, and
/// the last state main would have committed (a container that completes closes its private state, so that is the
/// state staged by the callback before; a fault stages its state).
/// </para>
/// </summary>
internal static class GraphHarness
{
    public const string ContainerNodeId = "node-flowchart";

    /// <summary>The corpus's pool: zero-padded so that ordinal order is hand-out order.</summary>
    public static IEnumerable<string> DefaultExecutionIds => Enumerable.Range(0, 96).Select(index => $"actexec-{index:D2}");

    public static GraphRun Run(
        string? joinPolicyKind,
        IReadOnlyList<Probe> children,
        IReadOnlyCollection<FlowchartConnection> connections,
        string? startNodeId = null,
        IReadOnlyDictionary<string, FlowchartNodeMetadata>? nodeMetadata = null,
        IEnumerable<string>? executionIds = null,
        string? triggerNodeId = null,
        params IFlowchartPolicy[] policies)
    {
        var options = joinPolicyKind is null ? new FlowchartOptions() : new FlowchartOptions { JoinPolicyKind = joinPolicyKind };
        var engine = FlowchartEngines.Create(options, policies);
        var ids = new Queue<string>(executionIds ?? DefaultExecutionIds);
        var containerId = ids.Dequeue();
        var probes = children.ToDictionary(child => child.NodeId, StringComparer.Ordinal);
        var order = new List<string>();
        var queue = new Queue<FlowchartScheduledNode>();
        FlowchartExecutionState? committed = null;
        FlowchartExecutionState? state = null;

        GraphRunStatus Apply(FlowchartStep step)
        {
            state = step.State;
            if (step.Kind != FlowchartStepKind.Complete) committed = step.State;
            foreach (var scheduled in step.Scheduled) queue.Enqueue(scheduled);
            return step.Kind switch
            {
                FlowchartStepKind.Complete => GraphRunStatus.Completed,
                FlowchartStepKind.Fault => GraphRunStatus.Faulted,
                _ => GraphRunStatus.Running,
            };
        }

        FlowchartGraph graph;
        GraphRunStatus status;
        try
        {
            graph = FlowchartGraph.From([.. children.Select(child => new FlowchartNode(child.NodeId, child.IsTrigger))], new FlowchartStructure(connections, startNodeId, nodeMetadata));
            status = Apply(engine.Start(graph, new FlowchartRun(containerId, triggerNodeId)));
        }
        catch (FlowchartExecutionException exception)
        {
            return new GraphRun(order, GraphRunStatus.Faulted, null, null, exception.Message);
        }

        string? faultMessage = null;
        while (status == GraphRunStatus.Running && queue.TryDequeue(out var next))
        {
            var executionId = ids.Dequeue();
            order.Add(next.NodeId);
            var run = new FlowchartRun(containerId, triggerNodeId);
            try
            {
                var probe = probes[next.NodeId];
                status = Apply(probe.Faults
                    ? engine.OnChildFaulted(graph, run, state, next.NodeId)
                    : engine.OnChildCompleted(graph, run, state, new FlowchartChildCompletion(next.NodeId, executionId, probe.Outcomes ?? [FlowchartOutcomes.Done], next.ExecutionPathId, next.ExecutionScopeId)));
            }
            catch (FlowchartExecutionException exception)
            {
                status = GraphRunStatus.Faulted;
                faultMessage = exception.Message;
            }
        }

        // A faulted container stays faulted with an incident while the children already scheduled run on (main's
        // harness: the sibling of a faulted branch still runs). Their completions reach no one.
        while (status == GraphRunStatus.Faulted && queue.TryDequeue(out var orphan))
        {
            ids.Dequeue();
            order.Add(orphan.NodeId);
        }

        var raw = committed is null ? null : JsonSerializer.Serialize(committed, FlowchartStatePersister.SerializerOptions);
        return new GraphRun(order, status, committed, raw, faultMessage);
    }

    public static FlowchartConnection Connection(string sourceNodeId, string targetNodeId, string? sourcePort = null) =>
        new(new FlowchartEndpoint(sourceNodeId, sourcePort), new FlowchartEndpoint(targetNodeId));
}

/// <summary>A leaf child: completes with its one outcome, or faults.</summary>
internal sealed record Probe(string NodeId, string[]? Outcomes = null, bool Faults = false, bool IsTrigger = false);

internal enum GraphRunStatus { Running, Completed, Faulted }

/// <summary>What a run observed: the children in the order they ran, the container's status, the last committed state.</summary>
internal sealed record GraphRun(
    IReadOnlyList<string> Order,
    GraphRunStatus Status,
    FlowchartExecutionState? FlowchartState,
    string? RawState,
    string? FaultMessage = null)
{
    public int RunCount(string nodeId) => Order.Count(id => StringComparer.Ordinal.Equals(id, nodeId));

    public string[] DiagnosticMessages(FlowchartDiagnosticKind kind)
    {
        Xunit.Assert.NotNull(FlowchartState);
        return FlowchartState.Diagnostics.Where(diagnostic => diagnostic.Kind == kind).Select(diagnostic => diagnostic.Message).ToArray();
    }
}

/// <summary>
/// Main's <c>ScriptedPolicy</c>: a policy node that schedules one caller-scripted target per invocation, the stand-in
/// for a gate whose decision changes between loop iterations.
/// </summary>
internal sealed class ScriptedPolicy(string policyKind, params string[] targetNodeIds) : IFlowchartPolicy
{
    private readonly Queue<string> _targets = new(targetNodeIds);

    public string PolicyKind { get; } = policyKind;
    public string DisplayName => $"Scripted ({PolicyKind})";

    public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
    {
        if (!_targets.TryDequeue(out var target))
            throw new InvalidOperationException($"Scripted policy '{PolicyKind}' ran out of scripted targets.");

        return new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: target)]);
    }
}
