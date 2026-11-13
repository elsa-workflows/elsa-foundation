using Kernel.Graph.Models;

namespace Kernel.Graph;

// The seam that replaces Elsa's runtime context. On main the engine reads its container from
// IRuntimeActivityExecutionContext, schedules children through it, and returns a RuntimeStructuralContinuation the
// runtime applies. Here the host (a native composite, a test harness) passes in what the engine reads and gets back
// what it decided: a pure step, (graph, state, one callback) -> (state, schedules, continuation). Nothing in this
// library knows a kernel, a store or an activity.

/// <summary>The outcome names the engine itself reacts to. On main these are <c>ActivityOutcomes</c>.</summary>
public static class FlowchartOutcomes
{
    public const string Done = "Done";
    public const string Break = "Break";
}

/// <summary>One node of a graph, as the engine sees it: an id, and whether a run may start there (a trigger).</summary>
public sealed record FlowchartNode(string NodeId, bool IsTrigger = false);

/// <summary>
/// One call into the engine: who the container is, which trigger started the run, and what the engine asked the host
/// to schedule. On main the engine called <c>context.ScheduleChildActivity</c>; here it appends to <see cref="Scheduled"/>.
/// </summary>
public sealed class FlowchartRun(string containerExecutionId, string? triggerNodeId = null)
{
    private readonly List<FlowchartScheduledNode> _scheduled = [];

    /// <summary>The container's own execution id: the producing id of last resort, as main's activity execution id.</summary>
    public string ContainerExecutionId { get; } = containerExecutionId;

    /// <summary>The node whose trigger started the run, if any (main's <c>context.TriggerNodeId</c>).</summary>
    public string? TriggerNodeId { get; } = triggerNodeId;

    /// <summary>What the engine asked the host to schedule in this call, in order.</summary>
    public IReadOnlyList<FlowchartScheduledNode> Scheduled => _scheduled;

    internal void ScheduleChildActivity(FlowchartScheduledNode node) => _scheduled.Add(node);
}

/// <summary>
/// A child the host must run. The host hands <see cref="ExecutionPathId"/> and <see cref="ExecutionScopeId"/> back in
/// the child's <see cref="FlowchartChildCompletion"/>: on main they travel as the work item's command metadata.
/// </summary>
public sealed record FlowchartScheduledNode(
    string NodeId,
    string ExecutionPathId,
    string ExecutionScopeId,
    string? IterationKey,
    string SchedulingActivityExecutionId,
    string SchedulingCause);

/// <summary>A child completed (main's <c>ActivityChildCompletedContext</c> plus the command metadata it carried).</summary>
public sealed record FlowchartChildCompletion(
    string CompletedChildExecutableNodeId,
    string CompletedChildActivityExecutionId,
    IReadOnlyCollection<string> OutcomeNames,
    string? ExecutionPathId = null,
    string? ExecutionScopeId = null);

public enum FlowchartStepKind
{
    /// <summary>The container keeps running: children are scheduled or paths wait (main's <c>Defer</c>).</summary>
    Defer,
    /// <summary>The container completes with <see cref="FlowchartStep.Outcome"/>.</summary>
    Complete,
    /// <summary>The container faults with <see cref="FlowchartStep.Fault"/>.</summary>
    Fault
}

public sealed record FlowchartFault(string Code, string Message);

/// <summary>
/// What one call decided. <see cref="State"/> is what main stages: pruned for persistence (#382). Main reloads the
/// staged state at the next callback, so a host passes exactly this state back in.
/// </summary>
public sealed record FlowchartStep(
    FlowchartStepKind Kind,
    FlowchartExecutionState State,
    IReadOnlyList<FlowchartScheduledNode> Scheduled,
    string? Outcome = null,
    FlowchartFault? Fault = null);
