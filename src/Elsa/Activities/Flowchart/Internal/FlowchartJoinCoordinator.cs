using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using static Elsa.Activities.Flowchart.Internal.FlowchartScheduler;
using static Elsa.Activities.Flowchart.Internal.FlowchartStateMutator;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Join and arrival bookkeeping for the Flowchart engine — the <c>FlowchartJoinHandler</c> named in #275.
/// Decides whether an inbound branch must wait at an implicit join, releases waiting joins once every
/// required branch has answered, fires the join (or a plain continuation) by consuming arrivals, completing
/// the waiting paths and scheduling the target, and — under a join policy that propagates dead paths — routes
/// the untaken half of a completion (ADR 0064).
/// <para>
/// The <em>decision</em> lives in the configured <see cref="IFlowchartJoinPolicy"/>
/// (<see cref="FlowchartOptions.JoinPolicyKind"/>); this type owns only the mechanics around it, so the two
/// semantics share one engine rather than forking it.
/// </para>
/// </summary>
public sealed class FlowchartJoinCoordinator(IFlowchartPolicyRegistry policyRegistry, FlowchartOptions options)
{
    private IFlowchartJoinPolicy? _joinPolicy;

    /// <summary>The configured join semantics. Resolved once, from the same registry every other policy lives in.</summary>
    public IFlowchartJoinPolicy JoinPolicy => _joinPolicy ??=
        policyRegistry.GetRequired(options.JoinPolicyKind) as IFlowchartJoinPolicy
        ?? throw new FlowchartExecutionException(
            $"Flowchart join policy '{options.JoinPolicyKind}' is registered but does not implement {nameof(IFlowchartJoinPolicy)}.");

    public FlowchartExecutionState ReleaseReadyWaitingJoins(IRuntimeActivityExecutionContext context, FlowchartExecutionState state, FlowchartGraph graph)
    {
        state = ReleaseReadyJoins(context, state, graph);
        return ReleaseStarvedJoins(context, state, graph);
    }

    private FlowchartExecutionState ReleaseReadyJoins(IRuntimeActivityExecutionContext context, FlowchartExecutionState state, FlowchartGraph graph)
    {
        foreach (var group in WaitingJoinKeys(state))
        {
            if (ShouldWaitForTarget(state, graph, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey))
                continue;

            var arrival = LiveArrivals(state, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey).FirstOrDefault();
            if (arrival is null)
                continue;

            state = FireJoinOrContinuation(
                context,
                state,
                graph,
                group.TargetNodeId,
                group.ExecutionScopeId,
                group.IterationKey,
                arrival.ProducingActivityExecutionId,
                arrival.ConnectionId);
        }

        return state;
    }

    /// <summary>
    /// Releases joins that are still waiting once the Flowchart has gone quiet — no active children at all, so
    /// no further arrival can ever reach them and the composite would otherwise defer forever with nothing
    /// scheduled to wake it.
    /// <para>
    /// This is the backstop for the arrivals that never come because nothing <em>completed</em>: a first-wins
    /// race cancels its losing branches, so the join behind them is owed an arrival no one will produce. The
    /// reachability policy answers that case by finding no live work in scope; a policy that decides purely
    /// from arrivals cannot see it, so quiescence has to force the decision instead. One group is released per
    /// pass — releasing schedules a child, which is exactly the evidence that the Flowchart is no longer quiet.
    /// </para>
    /// </summary>
    private FlowchartExecutionState ReleaseStarvedJoins(IRuntimeActivityExecutionContext context, FlowchartExecutionState state, FlowchartGraph graph)
    {
        // Each release completes at least one waiting path, so the number of them at entry bounds the loop.
        var bound = state.ExecutionPaths.Count(path => path.Status == ExecutionPathStatus.Waiting);
        for (var pass = 0; pass < bound && state.ActiveChildren.Count == 0; pass++)
        {
            var waiting = WaitingJoinKeys(state);
            if (waiting.Length == 0)
                break;

            var group = waiting[0];
            var missing = MissingInboundSourceIds(state, graph, group);
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.DeadPath, group.TargetNodeId, null, null, group.ExecutionScopeId,
                $"Flowchart released join '{group.TargetNodeId}' because no work remains that could produce the outstanding inbound branch(es): {string.Join(", ", missing)}.");

            var arrival = LiveArrivals(state, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey).FirstOrDefault();
            state = arrival is null
                ? CarryDeadnessThrough(context, state, graph, group, schedulingActivityExecutionId: null)
                : FireJoinOrContinuation(context, state, graph, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey, arrival.ProducingActivityExecutionId, arrival.ConnectionId);
        }

        return state;
    }

    public FlowchartExecutionState FireJoinOrContinuation(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        string targetNodeId,
        string executionScopeId,
        string? iterationKey,
        string schedulingActivityExecutionId,
        string? connectionId)
    {
        var arrivals = PendingArrivals(state, targetNodeId, executionScopeId, iterationKey).ToArray();
        state = ConsumeArrivals(state, arrivals);

        foreach (var waitingPath in WaitingPathsAt(state, targetNodeId, executionScopeId, iterationKey))
            state = UpdatePath(state, waitingPath with { Status = ExecutionPathStatus.Completed });

        var scheduledPath = NewPath(state, null, executionScopeId, targetNodeId, connectionId, schedulingActivityExecutionId, ExecutionPathStatus.Active, iterationKey);
        state = AddPath(state, scheduledPath);
        state = FlowchartDiagnosticAccumulator.Add(state, arrivals.Length > 1 ? FlowchartDiagnosticKind.Joined : FlowchartDiagnosticKind.Scheduled, targetNodeId, connectionId, scheduledPath.ExecutionPathId, executionScopeId,
            DescribeFiring(targetNodeId, arrivals));

        return ScheduleNode(context, state, targetNodeId, scheduledPath.ExecutionPathId, executionScopeId, scheduledPath.IterationKey, schedulingActivityExecutionId, arrivals.Length > 1 ? "join" : "continuation");
    }

    /// <summary>
    /// Routes the half of a completion the outcomes did <b>not</b> take: every outbound connection that is not
    /// in <paramref name="takenConnectionIds"/> emits a dead arrival, and deadness carries forward through any
    /// node whose every inbound has now answered dead. A no-op under a join policy that derives deadness
    /// instead of representing it, which is what keeps the two semantics on one engine.
    /// </summary>
    public FlowchartExecutionState RouteUntakenOutbound(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        string sourceNodeId,
        string executionScopeId,
        string? iterationKey,
        IReadOnlySet<string> takenConnectionIds,
        string producingActivityExecutionId)
    {
        if (!JoinPolicy.PropagatesDeadPaths)
            return state;

        foreach (var connection in graph.GetOutboundConnections(sourceNodeId))
        {
            if (takenConnectionIds.Contains(graph.GetConnectionId(connection)))
                continue;

            state = EmitDeadArrival(context, state, graph, connection, executionScopeId, iterationKey, producingActivityExecutionId);
        }

        return ReleaseReadyWaitingJoins(context, state, graph);
    }

    /// <summary>
    /// Records one dead arrival and immediately re-decides its target. A dead arrival deliberately creates no
    /// <see cref="ExecutionPathStatus.Waiting"/> path: deadness must never keep the Flowchart alive, and a
    /// target holding nothing but dead arrivals has no live branch whose progress could be blocked. The target
    /// is re-decided here rather than later because that is the only moment it can change — an arrival is the
    /// only input a join has.
    /// </summary>
    private FlowchartExecutionState EmitDeadArrival(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        FlowchartConnection connection,
        string executionScopeId,
        string? iterationKey,
        string producingActivityExecutionId)
    {
        var connectionId = graph.GetConnectionId(connection);

        // Absorption at the loop boundary — the failure mode ADR 0064 says to test hardest. A backward edge is
        // where the next iteration begins, and its target is a loop head, which by the ambiguous-loopback rule
        // has exactly one inbound and therefore never joins. Emitting here would either mint an iteration scope
        // for an iteration that is not happening, or leave a dead arrival keyed to a future iteration where a
        // live branch could pair with it. Neither is a token that exists, so nothing crosses.
        if (graph.IsBackwardEdge(connection.Source.NodeId, connection.Target.NodeId))
        {
            return FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.DeadPath, connection.Target.NodeId, connectionId, null, executionScopeId,
                $"Flowchart absorbed the dead path over loop-closing connection '{connectionId}': a dead path never crosses into the next iteration.");
        }

        var deadPath = NewPath(state, null, executionScopeId, connection.Target.NodeId, connectionId, producingActivityExecutionId, ExecutionPathStatus.Completed, iterationKey);
        state = AddPath(state, deadPath);
        state = AddArrival(state, deadPath, connection, connectionId, producingActivityExecutionId, FlowchartArrivalStatus.Dead);
        state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.DeadPath, connection.Target.NodeId, connectionId, deadPath.ExecutionPathId, executionScopeId,
            $"Flowchart marked connection '{connectionId}' dead because node '{connection.Source.NodeId}' completed without taking it.");

        var group = new JoinKey(connection.Target.NodeId, executionScopeId, iterationKey);
        if (ShouldWaitForTarget(state, graph, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey))
            return state;

        if (LiveArrivals(state, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey).Any())
            return FireJoinOrContinuation(context, state, graph, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey, producingActivityExecutionId, connectionId);

        return CarryDeadnessThrough(context, state, graph, group, producingActivityExecutionId);
    }

    /// <summary>
    /// A node whose every inbound answered dead is not scheduled: its arrivals are consumed and dead arrivals
    /// are emitted on <b>all</b> of its outbound connections, carrying the deadness to the next join. All of
    /// them, because a node that never runs produces no outcome to select between.
    /// <para>
    /// This terminates: the forward projection is acyclic, and a node carries deadness through at most once per
    /// <c>(node, scope, iteration)</c> key — it takes every inbound having answered to get here, which happens
    /// once.
    /// </para>
    /// </summary>
    private FlowchartExecutionState CarryDeadnessThrough(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        JoinKey group,
        string? schedulingActivityExecutionId)
    {
        var arrivals = PendingArrivals(state, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey).ToArray();
        state = ConsumeArrivals(state, arrivals);

        foreach (var waitingPath in WaitingPathsAt(state, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey))
            state = UpdatePath(state, waitingPath with { Status = ExecutionPathStatus.Completed });

        state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.DeadPath, group.TargetNodeId, null, null, group.ExecutionScopeId,
            $"Flowchart did not schedule node '{group.TargetNodeId}': all {arrivals.Length} inbound arrival(s) were dead, so the deadness carries on to its successors.");

        var producingActivityExecutionId = schedulingActivityExecutionId
                                           ?? arrivals.Select(arrival => arrival.ProducingActivityExecutionId).FirstOrDefault()
                                           ?? context.ActivityExecutionState.Execution.ActivityExecutionId;

        foreach (var connection in graph.GetOutboundConnections(group.TargetNodeId))
            state = EmitDeadArrival(context, state, graph, connection, group.ExecutionScopeId, group.IterationKey, producingActivityExecutionId);

        return state;
    }

    public bool ShouldWaitForTarget(FlowchartExecutionState state, FlowchartGraph graph, string targetNodeId, string executionScopeId, string? iterationKey)
    {
        var policyKind = graph.GetNodeMetadata(targetNodeId).PolicyKind;
        if (StringComparer.Ordinal.Equals(policyKind, FlowchartPolicyKinds.Merge))
            return false;

        var inboundConnections = graph.GetInboundConnections(targetNodeId);
        if (inboundConnections.Count <= 1)
            return false;

        return JoinPolicy.ShouldWait(new FlowchartJoinContext(
            state,
            inboundConnections,
            targetNodeId,
            executionScopeId,
            iterationKey,
            graph.CanReachForward));
    }

    /// <summary>Unconsumed arrivals at a key that carried a live token.</summary>
    public static IEnumerable<FlowchartArrival> LiveArrivals(FlowchartExecutionState state, string targetNodeId, string executionScopeId, string? iterationKey) =>
        PendingArrivals(state, targetNodeId, executionScopeId, iterationKey)
            .Where(arrival => arrival.Status == FlowchartArrivalStatus.Arrived);

    /// <summary>
    /// Unconsumed arrivals at a key, live and dead alike. Firing consumes both: leaving a dead arrival behind
    /// is precisely how a path from iteration <i>n</i> would end up satisfying a join in iteration <i>n+1</i>.
    /// </summary>
    public static IEnumerable<FlowchartArrival> PendingArrivals(FlowchartExecutionState state, string targetNodeId, string executionScopeId, string? iterationKey) =>
        state.Arrivals.Where(arrival =>
            arrival.Status != FlowchartArrivalStatus.Consumed &&
            StringComparer.Ordinal.Equals(arrival.TargetNodeId, targetNodeId) &&
            StringComparer.Ordinal.Equals(arrival.ExecutionScopeId, executionScopeId) &&
            StringComparer.Ordinal.Equals(arrival.IterationKey, iterationKey));

    private static FlowchartExecutionState ConsumeArrivals(FlowchartExecutionState state, IReadOnlyCollection<FlowchartArrival> arrivals)
    {
        foreach (var arrival in arrivals)
        {
            state = UpdateArrival(state, arrival with { Status = FlowchartArrivalStatus.Consumed });
            var arrivalPath = state.ExecutionPaths.FirstOrDefault(path => StringComparer.Ordinal.Equals(path.ExecutionPathId, arrival.ExecutionPathId));
            if (arrivalPath is not null)
                state = UpdatePath(state, arrivalPath with { Status = ExecutionPathStatus.Completed });
        }

        return state;
    }

    private static ExecutionPath[] WaitingPathsAt(FlowchartExecutionState state, string targetNodeId, string executionScopeId, string? iterationKey) =>
        state.ExecutionPaths.Where(path =>
            path.Status == ExecutionPathStatus.Waiting &&
            StringComparer.Ordinal.Equals(path.CurrentNodeId, targetNodeId) &&
            StringComparer.Ordinal.Equals(path.ExecutionScopeId, executionScopeId) &&
            StringComparer.Ordinal.Equals(path.IterationKey, iterationKey)).ToArray();

    private static JoinKey[] WaitingJoinKeys(FlowchartExecutionState state) =>
        state.ExecutionPaths
            .Where(path => path.Status == ExecutionPathStatus.Waiting && path.CurrentNodeId is not null)
            .Select(path => new JoinKey(path.CurrentNodeId!, path.ExecutionScopeId, path.IterationKey))
            .Distinct()
            .ToArray();

    private static string[] MissingInboundSourceIds(FlowchartExecutionState state, FlowchartGraph graph, JoinKey group)
    {
        var answered = PendingArrivals(state, group.TargetNodeId, group.ExecutionScopeId, group.IterationKey)
            .Select(arrival => arrival.SourceNodeId)
            .ToHashSet(StringComparer.Ordinal);

        return graph.GetInboundConnections(group.TargetNodeId)
            .Select(inbound => inbound.Source.NodeId)
            .Where(sourceNodeId => !answered.Contains(sourceNodeId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string DescribeFiring(string targetNodeId, IReadOnlyCollection<FlowchartArrival> arrivals)
    {
        if (arrivals.Count <= 1)
            return $"Flowchart scheduled node '{targetNodeId}'.";

        var deadCount = arrivals.Count(arrival => arrival.Status == FlowchartArrivalStatus.Dead);
        return deadCount == 0
            ? $"Implicit join '{targetNodeId}' fired after {arrivals.Count} active arrival(s)."
            : $"Implicit join '{targetNodeId}' fired after {arrivals.Count} arrival(s), {deadCount} of them dead.";
    }

    /// <summary>The <c>(node, scope, iteration)</c> partition every arrival and join decision is keyed on.</summary>
    private readonly record struct JoinKey(string TargetNodeId, string ExecutionScopeId, string? IterationKey);
}
