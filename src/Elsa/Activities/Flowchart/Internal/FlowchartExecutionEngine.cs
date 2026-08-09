using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using static Elsa.Activities.Flowchart.Internal.FlowchartScheduler;
using static Elsa.Activities.Flowchart.Internal.FlowchartScopeResolver;
using static Elsa.Activities.Flowchart.Internal.FlowchartStateMutator;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Orchestrates Flowchart composite execution across its three entry points (<see cref="StartAsync"/>,
/// <see cref="OnChildCompletedAsync"/>, <see cref="OnChildFaultedAsync"/>). It owns only the dispatch
/// decisions — resolving the completing child's path/scope, routing to the policy applier or implicit-join
/// path, break/race/fault handling, and composite completion — and delegates the mechanics to focused
/// collaborators: <see cref="FlowchartJoinCoordinator"/> (join/arrival bookkeeping),
/// <see cref="FlowchartPolicyApplier"/> (policy-decision scheduling), <see cref="FlowchartStatePersister"/>
/// (load/stage + #382 prune), plus the static <see cref="FlowchartStateMutator"/>,
/// <see cref="FlowchartScopeResolver"/>, <see cref="FlowchartScheduler"/>, and
/// <see cref="FlowchartDiagnosticAccumulator"/> helpers. All record ids remain a pure function of
/// <see cref="FlowchartExecutionState.Sequence"/>, whose only mutation home is <see cref="FlowchartStateMutator"/>.
/// </summary>
public sealed class FlowchartExecutionEngine(
    FlowchartReachabilityAnalyzer reachabilityAnalyzer,
    IFlowchartPolicyRegistry policyRegistry,
    FlowchartJoinCoordinator joinCoordinator,
    FlowchartPolicyApplier policyApplier,
    FlowchartStatePersister persister)
{
    public const string StateTypeAlias = "Elsa.Flowchart.ExecutionState";
    public const int StateSchemaVersion = 1;
    public const string ParentActivityExecutionIdMetadataKey = "flowchart.parentActivityExecutionId";
    public const string ExecutionPathIdMetadataKey = "flowchart.executionPathId";
    public const string ExecutionScopeIdMetadataKey = "flowchart.executionScopeId";
    public const string SchedulingCauseMetadataKey = "flowchart.schedulingCause";
    public const string TargetNodeIdMetadataKey = "flowchart.targetNodeId";

    public ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var graph = context.ExecutableNode.GetOrAddRoutingStructure(FlowchartGraph.From);
        reachabilityAnalyzer.ValidateNoAmbiguousLoopbacks(graph);
        var startNode = graph.SelectStartNode();

        if (startNode is null)
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        var state = FlowchartStatePersister.LoadState(context.ActivityExecutionState) ?? FlowchartStatePersister.CreateInitialState(startNode.ExecutableNodeId);
        var rootPath = state.ExecutionPaths.SingleOrDefault(path => path.ExecutionPathId == "path:root")
            ?? throw new InvalidOperationException($"Root execution path 'path:root' not found. ActivityExecutionId='{context.ActivityExecutionState.Execution.ActivityExecutionId}'.");
        state = ScheduleNode(context, state, startNode.ExecutableNodeId, rootPath.ExecutionPathId, rootPath.ExecutionScopeId, rootPath.IterationKey, context.ActivityExecutionState.Execution.ActivityExecutionId, "start");
        return ValueTask.FromResult(persister.StageState(RuntimeStructuralContinuation.Defer, state));
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completionContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completionContext);

        var graph = context.ExecutableNode.GetOrAddRoutingStructure(FlowchartGraph.From);
        var state = FlowchartStatePersister.LoadState(context.ActivityExecutionState) ?? FlowchartStatePersister.CreateInitialState(graph.SelectStartNode()?.ExecutableNodeId);
        var pathId = ResolveExecutionPathId(context, state, completionContext.CompletedChildExecutableNodeId);
        var path = state.ExecutionPaths.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.ExecutionPathId, pathId))
                   ?? throw new FlowchartExecutionException($"Flowchart execution path '{pathId}' was not found for completed child '{completionContext.CompletedChildExecutableNodeId}'.");
        var scopeId = ResolveExecutionScopeId(context, path);
        var scope = state.Scopes.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.ExecutionScopeId, scopeId))
                    ?? throw new FlowchartExecutionException($"Flowchart execution scope '{scopeId}' was not found for completed child '{completionContext.CompletedChildExecutableNodeId}'.");

        state = RemoveActiveChild(state, path.ExecutionPathId, completionContext.CompletedChildExecutableNodeId);
        if (path.Status == ExecutionPathStatus.Canceled || scope.Status == ExecutionScopeStatus.Canceled)
        {
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Canceled, completionContext.CompletedChildExecutableNodeId, path.IncomingConnectionId, path.ExecutionPathId, path.ExecutionScopeId, $"Flowchart ignored completion for canceled path '{path.ExecutionPathId}'.");
            return ValueTask.FromResult(PersistAndMaybeComplete(state));
        }

        // Break propagation (#304): when any path inside the Flowchart reaches a Break outcome, the whole
        // Flowchart ends — it completes itself with the Break outcome so the nearest enclosing loop
        // (For/ForEach/While/Do) ends early, mirroring the linear/branch composites (Sequence/If/Switch).
        // The breaking path is marked completed and every other in-flight path (e.g. sibling branches of a
        // parallel fork that has not yet joined) is canceled so no further nodes are scheduled; the late
        // completions of those canceled children are ignored because the Flowchart's activity execution is
        // no longer Running by then. Matched by outcome name so the Flowchart takes no dependency on the
        // (separate) Break activity module.
        if (completionContext.OutcomeNames.Contains(ActivityOutcomes.Break, StringComparer.Ordinal))
        {
            state = UpdatePath(state, path with
            {
                Status = ExecutionPathStatus.Completed,
                CurrentNodeId = completionContext.CompletedChildExecutableNodeId,
                LastOutcomeNames = completionContext.OutcomeNames
            });
            state = CancelPendingPathsForBreak(state, path, completionContext.CompletedChildExecutableNodeId);
            return ValueTask.FromResult(persister.StageState(RuntimeStructuralContinuation.Complete(ActivityOutcomes.Break), state));
        }

        if (scope.Kind == ExecutionScopeKind.Race && scope.Status == ExecutionScopeStatus.Active)
            state = CompleteRaceScope(state, scope, path);
        var continuationScope = ResolveContinuationScope(state, scope);

        var sourceMetadata = graph.GetNodeMetadata(completionContext.CompletedChildExecutableNodeId);
        if (sourceMetadata.PolicyKind is { } policyKind && !IsJoinPolicy(policyKind))
        {
            var policy = policyRegistry.GetRequired(policyKind);
            var policyContext = new FlowchartPolicyContext(
                FlowchartPolicyTrigger.ChildCompleted,
                state,
                graph.Connections,
                completionContext.CompletedChildExecutableNodeId,
                completionContext.OutcomeNames);
            FlowchartPolicyDecision decision;
            try
            {
                decision = policy.Execute(policyContext);
                var defaultScheduleScopeId = StringComparer.Ordinal.Equals(continuationScope.ExecutionScopeId, path.ExecutionScopeId) ? null : continuationScope.ExecutionScopeId;
                state = policyApplier.ApplyDecision(context, state, graph, decision, path, completionContext.CompletedChildActivityExecutionId, policyKind, defaultScheduleScopeId);
            }
            catch (FlowchartExecutionException exception)
            {
                state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.PolicyFailure, completionContext.CompletedChildExecutableNodeId, null, path.ExecutionPathId, path.ExecutionScopeId, exception.Message);
                return ValueTask.FromResult(persister.StageState(
                    RuntimeStructuralContinuation.Faulted(new ActivityFault("flowchart.policy.failed", exception.Message)),
                    state));
            }

            var currentPath = state.ExecutionPaths.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.ExecutionPathId, path.ExecutionPathId))
                ?? throw new InvalidOperationException($"Execution path '{path.ExecutionPathId}' not found. ActivityExecutionId='{context.ActivityExecutionState.Execution.ActivityExecutionId}'.");
            if (currentPath.Status == ExecutionPathStatus.Active)
                state = UpdatePath(state, currentPath with
                {
                    Status = ExecutionPathStatus.Completed,
                    CurrentNodeId = completionContext.CompletedChildExecutableNodeId,
                    LastOutcomeNames = completionContext.OutcomeNames
                });
            return ValueTask.FromResult(PersistAndMaybeComplete(state));
        }

        var outboundConnections = graph.SelectOutboundConnections(completionContext.CompletedChildExecutableNodeId, completionContext.OutcomeNames);
        if (outboundConnections.Select(connection => connection.Target.NodeId).Distinct(StringComparer.Ordinal).Count() != outboundConnections.Count)
            throw new FlowchartExecutionException($"Flowchart completion for child executable node '{completionContext.CompletedChildExecutableNodeId}' contains duplicate connection targets.");

        if (outboundConnections.Count == 0)
        {
            state = UpdatePath(state, path with
            {
                Status = ExecutionPathStatus.Completed,
                CurrentNodeId = completionContext.CompletedChildExecutableNodeId,
                LastOutcomeNames = completionContext.OutcomeNames
            });
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Completed, completionContext.CompletedChildExecutableNodeId, null, path.ExecutionPathId, path.ExecutionScopeId, $"Flowchart path '{path.ExecutionPathId}' completed at terminal node '{completionContext.CompletedChildExecutableNodeId}'.");
            // A node whose outcome matched no connection is still a completion that took none of the ones it
            // has, so its outbound edges are dead even though nothing was scheduled from it.
            state = RouteUntakenOutbound(context, state, graph, completionContext, continuationScope, new HashSet<string>(StringComparer.Ordinal));
            state = joinCoordinator.ReleaseReadyWaitingJoins(context, state, graph);

            return ValueTask.FromResult(PersistAndMaybeComplete(state));
        }

        var scheduledTargets = new HashSet<string>(StringComparer.Ordinal);
        var takenConnectionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in outboundConnections)
        {
            var connectionId = graph.GetConnectionId(connection);
            takenConnectionIds.Add(connectionId);
            var targetScope = ResolveTargetScope(reachabilityAnalyzer, state, graph, continuationScope, connection);
            if (state.Scopes.All(existing => !StringComparer.Ordinal.Equals(existing.ExecutionScopeId, targetScope.ExecutionScopeId)))
                state = AddScope(state, targetScope);
            var targetIterationKey = ResolveIterationKey(state, targetScope.ExecutionScopeId);
            var arrivalPath = NewPath(
                state,
                path.ExecutionPathId,
                targetScope.ExecutionScopeId,
                connection.Target.NodeId,
                connectionId,
                completionContext.CompletedChildActivityExecutionId,
                ExecutionPathStatus.Active,
                targetIterationKey);
            state = AddPath(state, arrivalPath);
            state = AddArrival(state, arrivalPath, connection, connectionId, completionContext.CompletedChildActivityExecutionId);

            if (joinCoordinator.ShouldWaitForTarget(state, graph, connection.Target.NodeId, targetScope.ExecutionScopeId, targetIterationKey))
            {
                state = UpdatePath(state, arrivalPath with { Status = ExecutionPathStatus.Waiting });
                state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Waiting, connection.Target.NodeId, connectionId, arrivalPath.ExecutionPathId, targetScope.ExecutionScopeId, $"Flowchart path '{arrivalPath.ExecutionPathId}' is waiting at implicit join '{connection.Target.NodeId}'.");
                continue;
            }

            if (scheduledTargets.Add(connection.Target.NodeId))
                state = joinCoordinator.FireJoinOrContinuation(context, state, graph, connection.Target.NodeId, targetScope.ExecutionScopeId, targetIterationKey, completionContext.CompletedChildActivityExecutionId, connectionId);
        }

        // The taken edges are routed above; the rest of the completion is the edges it did not take. Routed
        // after them so the live branches keep the scheduling order they have always had.
        state = RouteUntakenOutbound(context, state, graph, completionContext, continuationScope, takenConnectionIds);

        state = UpdatePath(state, path with
        {
            Status = ExecutionPathStatus.Completed,
            CurrentNodeId = completionContext.CompletedChildExecutableNodeId,
            LastOutcomeNames = completionContext.OutcomeNames
        });
        state = joinCoordinator.ReleaseReadyWaitingJoins(context, state, graph);

        return ValueTask.FromResult(PersistAndMaybeComplete(state));
    }

    /// <summary>
    /// Handles a terminal fault of one Flowchart child (#308). A faulted child cannot complete, so its execution
    /// path — and any downstream join that requires it (a Flowchart join needs <em>every</em> inbound branch) —
    /// can never proceed. Rather than leave the Flowchart Running forever (waiting on a completion that will never
    /// arrive), the Flowchart faults deterministically:
    /// it records a <see cref="FlowchartDiagnosticKind.Faulted"/> diagnostic, persists it, then returns a fault
    /// decision so the runtime surfaces a composite incident on the Flowchart — mirroring the <c>Parallel</c> fork/join
    /// composite's default-threshold behavior, where a single faulted branch faults the join. The faulted leaf
    /// keeps its own blocking incident regardless.
    /// </summary>
    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext faultContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faultContext);

        var graph = context.ExecutableNode.GetOrAddRoutingStructure(FlowchartGraph.From);
        var state = FlowchartStatePersister.LoadState(context.ActivityExecutionState) ?? FlowchartStatePersister.CreateInitialState(graph.SelectStartNode()?.ExecutableNodeId);

        var faultedNodeId = faultContext.FaultedChildExecutableNodeId;
        var faultedChild = state.ActiveChildren.FirstOrDefault(child => StringComparer.Ordinal.Equals(child.NodeId, faultedNodeId));
        if (faultedChild is not null)
            state = RemoveActiveChild(state, faultedChild.ExecutionPathId, faultedNodeId);

        var message = $"Flowchart faulted because child node '{faultedNodeId}' faulted: a faulted child cannot complete, so its execution path — and any downstream join that requires it (a Flowchart join needs every inbound branch) — can no longer proceed.";
        state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Faulted, faultedNodeId, null, faultedChild?.ExecutionPathId, faultedChild?.ExecutionScopeId, message);
        return ValueTask.FromResult(persister.StageState(
            RuntimeStructuralContinuation.Faulted(new ActivityFault("flowchart.child.faulted", message)),
            state));
    }

    /// <summary>
    /// Hands the untaken outbound connections of a completing child to the join coordinator. Dead arrivals stay
    /// in the continuation scope and never cross a loop-closing edge, so the emitting scope and iteration key
    /// are the ones the live arrivals of the same completion carry.
    /// </summary>
    private FlowchartExecutionState RouteUntakenOutbound(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        ActivityChildCompletedContext completionContext,
        ExecutionScope continuationScope,
        IReadOnlySet<string> takenConnectionIds) =>
        joinCoordinator.RouteUntakenOutbound(
            context,
            state,
            graph,
            completionContext.CompletedChildExecutableNodeId,
            continuationScope.ExecutionScopeId,
            ResolveIterationKey(state, continuationScope.ExecutionScopeId),
            takenConnectionIds,
            completionContext.CompletedChildActivityExecutionId);

    private RuntimeStructuralContinuation PersistAndMaybeComplete(FlowchartExecutionState state)
    {
        if (state.ActiveChildren.Count == 0 && state.ExecutionPaths.All(path => path.Status != ExecutionPathStatus.Waiting && path.Status != ExecutionPathStatus.Active))
        {
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Completed, null, null, null, state.RootExecutionScopeId, "Flowchart completed because no active or waiting execution paths remain.");
            return persister.StageState(RuntimeStructuralContinuation.Complete(), state);
        }

        return persister.StageState(RuntimeStructuralContinuation.Defer, state);
    }

    private static string ResolveExecutionPathId(IRuntimeActivityExecutionContext context, FlowchartExecutionState state, string completedNodeId)
    {
        if (context.SchedulerWorkItem.CommandMetadata.TryGetValue(ExecutionPathIdMetadataKey, out var metadataPathId) && !string.IsNullOrWhiteSpace(metadataPathId))
            return metadataPathId;

        var candidates = state.ExecutionPaths
            .Where(path => path.Status == ExecutionPathStatus.Active && StringComparer.Ordinal.Equals(path.CurrentNodeId, completedNodeId))
            .ToArray();

        if (candidates.Length == 1)
            return candidates[0].ExecutionPathId;

        throw new FlowchartExecutionException($"Unable to resolve Flowchart execution path for completed child node '{completedNodeId}'.");
    }

    private static string ResolveExecutionScopeId(IRuntimeActivityExecutionContext context, ExecutionPath path)
    {
        if (context.SchedulerWorkItem.CommandMetadata.TryGetValue(ExecutionScopeIdMetadataKey, out var metadataScopeId) && !string.IsNullOrWhiteSpace(metadataScopeId))
            return metadataScopeId;

        return path.ExecutionScopeId;
    }

    private static bool IsJoinPolicy(string policyKind) =>
        StringComparer.Ordinal.Equals(policyKind, Internal.Policies.FlowchartPolicyKinds.ImplicitActivationJoin) ||
        StringComparer.Ordinal.Equals(policyKind, Internal.Policies.FlowchartPolicyKinds.ParallelJoin) ||
        StringComparer.Ordinal.Equals(policyKind, Internal.Policies.FlowchartPolicyKinds.InclusiveJoin);
}
