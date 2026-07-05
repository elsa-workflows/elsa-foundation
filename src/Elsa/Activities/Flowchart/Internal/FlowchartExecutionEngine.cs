using System.Text.Json;
using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using static Elsa.Activities.Flowchart.Internal.FlowchartScheduler;
using static Elsa.Activities.Flowchart.Internal.FlowchartStateMutator;

namespace Elsa.Activities.Flowchart.Internal;

public sealed class FlowchartExecutionEngine(
    FlowchartReachabilityAnalyzer reachabilityAnalyzer,
    IFlowchartPolicyRegistry policyRegistry,
    RuntimeCheckpointCommitter checkpointCommitter,
    IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
    TimeProvider? timeProvider = null)
{
    public const string StateMetadataKey = "elsa.flowchart.executionState";
    public const string ParentActivityExecutionIdMetadataKey = "flowchart.parentActivityExecutionId";
    public const string ExecutionPathIdMetadataKey = "flowchart.executionPathId";
    public const string ExecutionScopeIdMetadataKey = "flowchart.executionScopeId";
    public const string SchedulingCauseMetadataKey = "flowchart.schedulingCause";
    public const string TargetNodeIdMetadataKey = "flowchart.targetNodeId";
    private const string FlowchartStatePersistenceReason = "FlowchartStatePersistence";
    private const string RootPathId = "path:root";

    /// <summary>Maximum diagnostics retained in the persisted state blob (#382); oldest are dropped first.</summary>
    private const int DiagnosticsCap = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Start(IRuntimeActivityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var graph = FlowchartGraph.From(context.ExecutableNode);
        reachabilityAnalyzer.ValidateNoAmbiguousLoopbacks(graph);
        var startNode = graph.SelectStartNode();

        if (startNode is null)
        {
            context.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        var state = LoadState(context.ActivityExecutionState) ?? CreateInitialState(startNode.ExecutableNodeId);
        var rootPath = state.ExecutionPaths.SingleOrDefault(path => path.ExecutionPathId == "path:root")
            ?? throw new InvalidOperationException($"Root execution path 'path:root' not found. ActivityExecutionId='{context.ActivityExecutionState.Execution.ActivityExecutionId}'.");
        state = ScheduleNode(context, state, startNode.ExecutableNodeId, rootPath.ExecutionPathId, rootPath.ExecutionScopeId, rootPath.IterationKey, context.ActivityExecutionState.Execution.ActivityExecutionId, "start");
        SaveState(context, state);
    }

    public async ValueTask OnChildCompletedAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completionContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completionContext);

        var graph = FlowchartGraph.From(context.ExecutableNode);
        var state = LoadState(context.ActivityExecutionState) ?? CreateInitialState(graph.SelectStartNode()?.ExecutableNodeId);
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
            await PersistAndMaybeCompleteAsync(context, state);
            return;
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
            await SaveStateAsync(context, state);
            context.CompleteCompositeActivity([ActivityOutcomes.Break]);
            return;
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
                state = ApplyDecision(context, state, graph, decision, path, completionContext.CompletedChildActivityExecutionId, policyKind, defaultScheduleScopeId);
            }
            catch (FlowchartExecutionException exception)
            {
                state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.PolicyFailure, completionContext.CompletedChildExecutableNodeId, null, path.ExecutionPathId, path.ExecutionScopeId, exception.Message);
                await SaveStateAsync(context, state);
                throw;
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
            await PersistAndMaybeCompleteAsync(context, state);
            return;
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
            state = ReleaseReadyWaitingJoins(context, state, graph);

            await PersistAndMaybeCompleteAsync(context, state);
            return;
        }

        var scheduledTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in outboundConnections)
        {
            var connectionId = graph.GetConnectionId(connection);
            var targetScope = ResolveTargetScope(state, graph, continuationScope, connection);
            if (state.Scopes.All(existing => !StringComparer.Ordinal.Equals(existing.ExecutionScopeId, targetScope.ExecutionScopeId)))
                state = AddScope(state, targetScope);
            var targetIterationKey = targetScope.LoopIterationKey ?? path.IterationKey;
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

            if (ShouldWaitForTarget(state, graph, connection.Target.NodeId, targetScope.ExecutionScopeId, targetIterationKey))
            {
                state = UpdatePath(state, arrivalPath with { Status = ExecutionPathStatus.Waiting });
                state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Waiting, connection.Target.NodeId, connectionId, arrivalPath.ExecutionPathId, targetScope.ExecutionScopeId, $"Flowchart path '{arrivalPath.ExecutionPathId}' is waiting at implicit join '{connection.Target.NodeId}'.");
                continue;
            }

            if (scheduledTargets.Add(connection.Target.NodeId))
                state = FireJoinOrContinuation(context, state, graph, connection.Target.NodeId, targetScope.ExecutionScopeId, targetIterationKey, completionContext.CompletedChildActivityExecutionId, connectionId);
        }

        state = UpdatePath(state, path with
        {
            Status = ExecutionPathStatus.Completed,
            CurrentNodeId = completionContext.CompletedChildExecutableNodeId,
            LastOutcomeNames = completionContext.OutcomeNames
        });
        state = ReleaseReadyWaitingJoins(context, state, graph);

        await PersistAndMaybeCompleteAsync(context, state);
    }

    /// <summary>
    /// Handles a terminal fault of one Flowchart child (#308). A faulted child cannot complete, so its execution
    /// path — and any downstream join that requires it (a Flowchart join needs <em>every</em> inbound branch) —
    /// can never proceed. Rather than leave the Flowchart Running forever (waiting on a completion that will never
    /// arrive), the Flowchart faults deterministically:
    /// it records a <see cref="FlowchartDiagnosticKind.Faulted"/> diagnostic, persists it, then throws so the
    /// runtime surfaces a composite incident on the Flowchart — mirroring the <c>Parallel</c> fork/join
    /// composite's default-threshold behavior, where a single faulted branch faults the join. The faulted leaf
    /// keeps its own blocking incident regardless.
    /// </summary>
    public async ValueTask OnChildFaultedAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext faultContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faultContext);

        var graph = FlowchartGraph.From(context.ExecutableNode);
        var state = LoadState(context.ActivityExecutionState) ?? CreateInitialState(graph.SelectStartNode()?.ExecutableNodeId);

        var faultedNodeId = faultContext.FaultedChildExecutableNodeId;
        var faultedChild = state.ActiveChildren.FirstOrDefault(child => StringComparer.Ordinal.Equals(child.NodeId, faultedNodeId));
        if (faultedChild is not null)
            state = RemoveActiveChild(state, faultedChild.ExecutionPathId, faultedNodeId);

        var message = $"Flowchart faulted because child node '{faultedNodeId}' faulted: a faulted child cannot complete, so its execution path — and any downstream join that requires it (a Flowchart join needs every inbound branch) — can no longer proceed.";
        state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Faulted, faultedNodeId, null, faultedChild?.ExecutionPathId, faultedChild?.ExecutionScopeId, message);
        await SaveStateAsync(context, state);

        throw new FlowchartExecutionException(message);
    }

    private async ValueTask PersistAndMaybeCompleteAsync(IRuntimeActivityExecutionContext context, FlowchartExecutionState state)
    {
        if (state.ActiveChildren.Count == 0 && state.ExecutionPaths.All(path => path.Status != ExecutionPathStatus.Waiting && path.Status != ExecutionPathStatus.Active))
        {
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Completed, null, null, null, state.RootExecutionScopeId, "Flowchart completed because no active or waiting execution paths remain.");
            await SaveStateAsync(context, state);
            context.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        await SaveStateAsync(context, state);
        context.DeferCompositeCompletion();
    }

    private FlowchartExecutionState ReleaseReadyWaitingJoins(IRuntimeActivityExecutionContext context, FlowchartExecutionState state, FlowchartGraph graph)
    {
        var groups = state.ExecutionPaths
            .Where(path => path.Status == ExecutionPathStatus.Waiting && path.CurrentNodeId is not null)
            .GroupBy(path => (path.CurrentNodeId!, path.ExecutionScopeId, path.IterationKey))
            .ToArray();

        foreach (var group in groups)
        {
            if (ShouldWaitForTarget(state, graph, group.Key.Item1, group.Key.ExecutionScopeId, group.Key.IterationKey))
                continue;

            var arrival = MatchingArrivals(state, group.Key.Item1, group.Key.ExecutionScopeId, group.Key.IterationKey).FirstOrDefault();
            if (arrival is null)
                continue;

            state = FireJoinOrContinuation(
                context,
                state,
                graph,
                group.Key.Item1,
                group.Key.ExecutionScopeId,
                group.Key.IterationKey,
                arrival.ProducingActivityExecutionId,
                arrival.ConnectionId);
        }

        return state;
    }

    private FlowchartExecutionState FireJoinOrContinuation(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        string targetNodeId,
        string executionScopeId,
        string? iterationKey,
        string schedulingActivityExecutionId,
        string? connectionId)
    {
        var arrivals = MatchingArrivals(state, targetNodeId, executionScopeId, iterationKey).ToArray();
        foreach (var arrival in arrivals)
        {
            state = UpdateArrival(state, arrival with { Status = FlowchartArrivalStatus.Consumed });
            var arrivalPath = state.ExecutionPaths.FirstOrDefault(path => StringComparer.Ordinal.Equals(path.ExecutionPathId, arrival.ExecutionPathId));
            if (arrivalPath is not null)
                state = UpdatePath(state, arrivalPath with { Status = ExecutionPathStatus.Completed });
        }

        foreach (var waitingPath in state.ExecutionPaths.Where(path =>
                     path.Status == ExecutionPathStatus.Waiting &&
                     StringComparer.Ordinal.Equals(path.CurrentNodeId, targetNodeId) &&
                     StringComparer.Ordinal.Equals(path.ExecutionScopeId, executionScopeId) &&
                     StringComparer.Ordinal.Equals(path.IterationKey, iterationKey)).ToArray())
            state = UpdatePath(state, waitingPath with { Status = ExecutionPathStatus.Completed });

        var scheduledPath = NewPath(state, null, executionScopeId, targetNodeId, connectionId, schedulingActivityExecutionId, ExecutionPathStatus.Active, iterationKey);
        state = AddPath(state, scheduledPath);
        state = FlowchartDiagnosticAccumulator.Add(state, arrivals.Length > 1 ? FlowchartDiagnosticKind.Joined : FlowchartDiagnosticKind.Scheduled, targetNodeId, connectionId, scheduledPath.ExecutionPathId, executionScopeId, arrivals.Length > 1
            ? $"Implicit join '{targetNodeId}' fired after {arrivals.Length} active arrival(s)."
            : $"Flowchart scheduled node '{targetNodeId}'.");

        return ScheduleNode(context, state, targetNodeId, scheduledPath.ExecutionPathId, executionScopeId, scheduledPath.IterationKey, schedulingActivityExecutionId, arrivals.Length > 1 ? "join" : "continuation");
    }

    private FlowchartExecutionState ApplyDecision(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        FlowchartPolicyDecision decision,
        ExecutionPath currentPath,
        string schedulingActivityExecutionId,
        string policyKind,
        string? defaultScheduleScopeId = null)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var scheduledNodes = new HashSet<string>(StringComparer.Ordinal);
        var firstWinsScopeId = ResolveFirstWinsScopeId(state, decision, currentPath, policyKind);
        if (firstWinsScopeId is not null)
        {
            var parentScopeId = defaultScheduleScopeId ?? currentPath.ExecutionScopeId;
            var scope = new ExecutionScope(
                firstWinsScopeId,
                ExecutionScopeKind.Race,
                parentExecutionScopeId: parentScopeId,
                createdByNodeId: currentPath.CurrentNodeId,
                ownerNodeId: currentPath.CurrentNodeId);
            state = AddScope(state, scope);
        }

        foreach (var diagnostic in decision.Diagnostics)
            state = FlowchartDiagnosticAccumulator.Append(state, diagnostic);

        foreach (var command in decision.Commands)
        {
            var effectiveCommand = command;
            if (command.Kind == FlowchartPolicyCommandKind.ScheduleNode)
            {
                if (firstWinsScopeId is not null && string.IsNullOrWhiteSpace(command.TargetExecutionScopeId))
                    effectiveCommand = command with { TargetExecutionScopeId = firstWinsScopeId };
                else if (!string.IsNullOrWhiteSpace(defaultScheduleScopeId) && string.IsNullOrWhiteSpace(command.TargetExecutionScopeId) && string.IsNullOrWhiteSpace(command.ExecutionScopeId))
                    effectiveCommand = command with { TargetExecutionScopeId = defaultScheduleScopeId };
            }
            state = command.Kind switch
            {
                FlowchartPolicyCommandKind.ScheduleNode => ApplyScheduleNodeCommand(context, state, graph, effectiveCommand, currentPath, schedulingActivityExecutionId, policyKind, scheduledNodes),
                FlowchartPolicyCommandKind.WriteDiagnostic => ApplyDiagnosticCommand(state, effectiveCommand, currentPath),
                FlowchartPolicyCommandKind.CompleteExecutionPath => UpdatePath(state, currentPath with { Status = ExecutionPathStatus.Completed }),
                FlowchartPolicyCommandKind.WaitExecutionPath => UpdatePath(state, currentPath with { Status = ExecutionPathStatus.Waiting }),
                FlowchartPolicyCommandKind.CancelExecutionPath => UpdatePath(state, currentPath with { Status = ExecutionPathStatus.Canceled }),
                _ => throw NewInvalidPolicyCommand(policyKind, $"Policy command '{effectiveCommand.Kind}' is not supported by this Flowchart execution engine slice.")
            };
        }

        return state;
    }

    private FlowchartExecutionState ApplyScheduleNodeCommand(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        FlowchartPolicyCommand command,
        ExecutionPath currentPath,
        string schedulingActivityExecutionId,
        string policyKind,
        HashSet<string> scheduledNodes)
    {
        if (string.IsNullOrWhiteSpace(command.NodeId))
            throw NewInvalidPolicyCommand(policyKind, "ScheduleNode command requires a node id.");
        var nodeId = command.NodeId;

        graph.GetRequiredNode(nodeId);
        if (!scheduledNodes.Add(nodeId))
            throw NewInvalidPolicyCommand(policyKind, $"Policy returned duplicate ScheduleNode command for node '{nodeId}'.");

        var executionScopeId = ResolveScheduleTargetScopeId(command, currentPath);
        if (state.Scopes.All(scope => !StringComparer.Ordinal.Equals(scope.ExecutionScopeId, executionScopeId)))
            throw NewInvalidPolicyCommand(policyKind, $"ScheduleNode command references unknown execution scope '{executionScopeId}'.");

        var iterationKey = currentPath.IterationKey;
        var connection = ResolveScheduleConnection(graph, command, currentPath, nodeId, policyKind);
        if (connection is not null && reachabilityAnalyzer.IsBackwardEdge(graph, connection.Source.NodeId, connection.Target.NodeId))
        {
            var currentScope = state.Scopes.FirstOrDefault(scope => StringComparer.Ordinal.Equals(scope.ExecutionScopeId, currentPath.ExecutionScopeId))
                ?? throw new InvalidOperationException($"Execution scope '{currentPath.ExecutionScopeId}' not found. ActivityExecutionId='{context.ActivityExecutionState.Execution.ActivityExecutionId}'.");
            var loopScope = ResolveTargetScope(state, graph, currentScope, connection);
            if (state.Scopes.All(existing => !StringComparer.Ordinal.Equals(existing.ExecutionScopeId, loopScope.ExecutionScopeId)))
                state = AddScope(state, loopScope);
            executionScopeId = loopScope.ExecutionScopeId;
            iterationKey = loopScope.LoopIterationKey;
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.LoopIteration, nodeId, graph.GetConnectionId(connection), currentPath.ExecutionPathId, executionScopeId, $"Flowchart created loop iteration scope '{executionScopeId}' for loopback to '{nodeId}'.");
        }

        if (connection is not null)
        {
            var connectionId = graph.GetConnectionId(connection);
            var arrivalPath = NewPath(state, currentPath.ExecutionPathId, executionScopeId, nodeId, connectionId, schedulingActivityExecutionId, ExecutionPathStatus.Active, iterationKey);
            state = AddPath(state, arrivalPath);
            state = AddArrival(state, arrivalPath, connection, connectionId, schedulingActivityExecutionId);

            if (ShouldWaitForTarget(state, graph, nodeId, executionScopeId, iterationKey))
            {
                state = UpdatePath(state, arrivalPath with { Status = ExecutionPathStatus.Waiting });
                return FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Waiting, nodeId, connectionId, arrivalPath.ExecutionPathId, executionScopeId, $"Flowchart policy '{policyKind}' is waiting at implicit join '{nodeId}'.");
            }

            return FireJoinOrContinuation(context, state, graph, nodeId, executionScopeId, iterationKey, schedulingActivityExecutionId, connectionId);
        }

        var scheduledPath = NewPath(state, currentPath.ExecutionPathId, executionScopeId, nodeId, command.ConnectionId, schedulingActivityExecutionId, ExecutionPathStatus.Active, iterationKey);
        state = AddPath(state, scheduledPath);
        state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Scheduled, nodeId, command.ConnectionId, scheduledPath.ExecutionPathId, executionScopeId, $"Flowchart policy '{policyKind}' scheduled node '{nodeId}'.");
        return ScheduleNode(context, state, nodeId, scheduledPath.ExecutionPathId, executionScopeId, scheduledPath.IterationKey, schedulingActivityExecutionId, $"policy:{policyKind}");
    }

    private static string ResolveScheduleTargetScopeId(FlowchartPolicyCommand command, ExecutionPath currentPath)
    {
        if (!string.IsNullOrWhiteSpace(command.TargetExecutionScopeId))
            return command.TargetExecutionScopeId;

        return string.IsNullOrWhiteSpace(command.ExecutionScopeId)
            ? currentPath.ExecutionScopeId
            : command.ExecutionScopeId;
    }

    private static FlowchartConnection? ResolveScheduleConnection(FlowchartGraph graph, FlowchartPolicyCommand command, ExecutionPath currentPath, string nodeId, string policyKind)
    {
        if (!string.IsNullOrWhiteSpace(command.ConnectionId))
        {
            var connection = graph.FindConnectionById(command.ConnectionId)
                             ?? throw NewInvalidPolicyCommand(policyKind, $"ScheduleNode command references unknown connection id '{command.ConnectionId}'.");

            if (!StringComparer.Ordinal.Equals(connection.Target.NodeId, nodeId))
                throw NewInvalidPolicyCommand(policyKind, $"ScheduleNode command connection id '{command.ConnectionId}' targets node '{connection.Target.NodeId}' instead of '{nodeId}'.");

            if (currentPath.CurrentNodeId is not null && !StringComparer.Ordinal.Equals(connection.Source.NodeId, currentPath.CurrentNodeId))
                throw NewInvalidPolicyCommand(policyKind, $"ScheduleNode command connection id '{command.ConnectionId}' starts at node '{connection.Source.NodeId}' instead of current node '{currentPath.CurrentNodeId}'.");

            return connection;
        }

        return currentPath.CurrentNodeId is null
            ? null
            : graph.FindConnection(currentPath.CurrentNodeId, nodeId);
    }

    private static FlowchartExecutionState ApplyDiagnosticCommand(FlowchartExecutionState state, FlowchartPolicyCommand command, ExecutionPath currentPath)
    {
        if (string.IsNullOrWhiteSpace(command.Message))
            throw new FlowchartExecutionException("WriteDiagnostic command requires a message.");

        return FlowchartDiagnosticAccumulator.Add(
            state,
            FlowchartDiagnosticKind.PolicyFailure,
            command.NodeId,
            command.ConnectionId,
            command.ExecutionPathId ?? currentPath.ExecutionPathId,
            command.ExecutionScopeId ?? currentPath.ExecutionScopeId,
            command.Message);
    }

    private static FlowchartExecutionException NewInvalidPolicyCommand(string policyKind, string reason) =>
        new($"Flowchart policy '{policyKind}' returned an invalid command. {reason}");

    private static string? ResolveFirstWinsScopeId(FlowchartExecutionState state, FlowchartPolicyDecision decision, ExecutionPath currentPath, string policyKind)
    {
        if (!StringComparer.Ordinal.Equals(policyKind, Internal.Policies.FlowchartPolicyKinds.FirstWins))
            return null;

        if (decision.Commands.All(command => command.Kind != FlowchartPolicyCommandKind.ScheduleNode))
            return null;

        return NewId(state, "scope");
    }

    private static ExecutionScope ResolveContinuationScope(FlowchartExecutionState state, ExecutionScope scope)
    {
        if (scope.Kind != ExecutionScopeKind.Race || string.IsNullOrWhiteSpace(scope.ParentExecutionScopeId))
            return scope;

        return state.Scopes.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.ExecutionScopeId, scope.ParentExecutionScopeId))
               ?? throw new FlowchartExecutionException($"Flowchart race parent scope '{scope.ParentExecutionScopeId}' was not found.");
    }

    private static FlowchartExecutionState CancelPendingPathsForBreak(FlowchartExecutionState state, ExecutionPath breakingPath, string breakNodeId)
    {
        state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Completed, breakNodeId, null, breakingPath.ExecutionPathId, breakingPath.ExecutionScopeId, $"Flowchart ended early because node '{breakNodeId}' completed with a Break outcome.");

        foreach (var pendingPath in state.ExecutionPaths
                     .Where(path =>
                         !StringComparer.Ordinal.Equals(path.ExecutionPathId, breakingPath.ExecutionPathId) &&
                         (path.Status == ExecutionPathStatus.Active || path.Status == ExecutionPathStatus.Waiting))
                     .ToArray())
        {
            state = UpdatePath(state, pendingPath with { Status = ExecutionPathStatus.Canceled });
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Canceled, pendingPath.CurrentNodeId, pendingPath.IncomingConnectionId, pendingPath.ExecutionPathId, pendingPath.ExecutionScopeId, $"Flowchart Break canceled pending path '{pendingPath.ExecutionPathId}'.");
        }

        return state with { ActiveChildren = [], Sequence = state.Sequence + 1 };
    }

    private static FlowchartExecutionState CompleteRaceScope(FlowchartExecutionState state, ExecutionScope raceScope, ExecutionPath winningPath)
    {
        foreach (var losingPath in state.ExecutionPaths
                     .Where(path =>
                         !StringComparer.Ordinal.Equals(path.ExecutionPathId, winningPath.ExecutionPathId) &&
                         StringComparer.Ordinal.Equals(path.ExecutionScopeId, raceScope.ExecutionScopeId) &&
                         (path.Status == ExecutionPathStatus.Active || path.Status == ExecutionPathStatus.Waiting))
                     .ToArray())
        {
            state = UpdatePath(state, losingPath with { Status = ExecutionPathStatus.Canceled });
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Canceled, losingPath.CurrentNodeId, losingPath.IncomingConnectionId, losingPath.ExecutionPathId, losingPath.ExecutionScopeId, $"Flowchart first-wins race canceled losing path '{losingPath.ExecutionPathId}'.");
        }

        state = state with
        {
            ActiveChildren = state.ActiveChildren
                .Where(child => !StringComparer.Ordinal.Equals(child.ExecutionScopeId, raceScope.ExecutionScopeId) || StringComparer.Ordinal.Equals(child.ExecutionPathId, winningPath.ExecutionPathId))
                .ToArray(),
            Sequence = state.Sequence + 1
        };
        return UpdateScope(state, raceScope with { Status = ExecutionScopeStatus.Completed });
    }

    private ExecutionScope ResolveTargetScope(FlowchartExecutionState state, FlowchartGraph graph, ExecutionScope currentScope, FlowchartConnection connection)
    {
        if (!reachabilityAnalyzer.IsBackwardEdge(graph, connection.Source.NodeId, connection.Target.NodeId))
            return currentScope;

        var iterationNumber = state.Scopes.Count(scope => scope.Kind == ExecutionScopeKind.LoopIteration && StringComparer.Ordinal.Equals(scope.OwnerNodeId, connection.Target.NodeId)) + 1;
        var iterationKey = $"{connection.Target.NodeId}:{iterationNumber}";
        var scope = new ExecutionScope(
            executionScopeId: NewId(state, "scope"),
            kind: ExecutionScopeKind.LoopIteration,
            parentExecutionScopeId: currentScope.ExecutionScopeId,
            createdByNodeId: connection.Source.NodeId,
            startConnectionId: graph.GetConnectionId(connection),
            ownerNodeId: connection.Target.NodeId,
            loopIterationKey: iterationKey);

        return scope;
    }

    private static bool ShouldWaitForImplicitJoin(FlowchartExecutionState state, FlowchartGraph graph, string targetNodeId, string executionScopeId, string? iterationKey)
    {
        var inboundConnections = graph.GetInboundConnections(targetNodeId);
        if (inboundConnections.Count <= 1)
            return false;

        var arrivedSourceIds = MatchingArrivals(state, targetNodeId, executionScopeId, iterationKey)
            .Select(arrival => arrival.SourceNodeId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var inboundConnection in inboundConnections)
        {
            if (arrivedSourceIds.Contains(inboundConnection.Source.NodeId))
                continue;

            if (state.ActiveChildren.Any(child => StringComparer.Ordinal.Equals(child.ExecutionScopeId, executionScopeId) && graph.CanReach(child.NodeId, inboundConnection.Source.NodeId)))
                return true;
        }

        return false;
    }

    private static bool ShouldWaitForTarget(FlowchartExecutionState state, FlowchartGraph graph, string targetNodeId, string executionScopeId, string? iterationKey)
    {
        var policyKind = graph.GetNodeMetadata(targetNodeId).PolicyKind;
        if (StringComparer.Ordinal.Equals(policyKind, Internal.Policies.FlowchartPolicyKinds.Merge))
            return false;

        return ShouldWaitForImplicitJoin(state, graph, targetNodeId, executionScopeId, iterationKey);
    }

    private static IEnumerable<FlowchartArrival> MatchingArrivals(FlowchartExecutionState state, string targetNodeId, string executionScopeId, string? iterationKey) =>
        state.Arrivals.Where(arrival =>
            arrival.Status == FlowchartArrivalStatus.Arrived &&
            StringComparer.Ordinal.Equals(arrival.TargetNodeId, targetNodeId) &&
            StringComparer.Ordinal.Equals(arrival.ExecutionScopeId, executionScopeId) &&
            StringComparer.Ordinal.Equals(arrival.IterationKey, iterationKey));

    private static FlowchartExecutionState CreateInitialState(string? startNodeId)
    {
        const string rootScopeId = "scope:root";
        const string rootPathId = "path:root";
        var rootScope = new ExecutionScope(rootScopeId, ExecutionScopeKind.Root, ownerNodeId: startNodeId);
        var rootPath = new ExecutionPath(rootPathId, rootScopeId, currentNodeId: startNodeId);
        return new FlowchartExecutionState(rootScopeId, [rootScope], [rootPath]);
    }

    private static FlowchartExecutionState? LoadState(ActivityExecutionState activityExecutionState)
    {
        if (!activityExecutionState.Metadata.TryGetValue(StateMetadataKey, out var serialized) || string.IsNullOrWhiteSpace(serialized))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FlowchartExecutionState>(serialized, SerializerOptions)
                   ?? throw new FlowchartExecutionException("Flowchart execution state metadata resolved to null.");
        }
        catch (FlowchartExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new FlowchartExecutionException("Flowchart execution state metadata is invalid.", exception);
        }
    }

    private void SaveState(IRuntimeActivityExecutionContext context, FlowchartExecutionState state)
    {
        SaveStateAsync(context, state)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Trims records that can never influence a future engine decision before the state is persisted
    /// (#382): without this, a loop-heavy flowchart re-serializes an ever-growing blob on every child
    /// completion — O(n²) in CPU and storage across n iterations.
    /// <list type="bullet">
    /// <item><b>Arrivals:</b> <see cref="FlowchartArrivalStatus.Consumed"/> arrivals are dropped
    /// unconditionally — the only arrival reader (<see cref="MatchingArrivals"/>) filters on
    /// <see cref="FlowchartArrivalStatus.Arrived"/>.</item>
    /// <item><b>Paths:</b> only <see cref="ExecutionPathStatus.Completed"/> paths are dropped, and then
    /// only unless they are (a) the root path, (b) referenced by an
    /// <see cref="FlowchartExecutionState.ActiveChildren"/> entry, or (c) referenced by a surviving
    /// arrival (belt-and-braces; such paths are Waiting and therefore retained anyway).
    /// <see cref="ExecutionPathStatus.Canceled"/>/<see cref="ExecutionPathStatus.Faulted"/> paths are
    /// <b>never</b> pruned: <see cref="CompleteRaceScope"/> strips losing branches from
    /// <c>ActiveChildren</c> while their activities may still be in flight, and the loser's late
    /// completion is absorbed via the by-id path lookup ("ignored completion for canceled path") — prune
    /// the record and that lookup faults the flowchart. Canceled/Faulted counts are bounded by graph
    /// structure (races, breaks, incidents), not by loop iterations, so the O(n²) growth fix is
    /// unaffected. <see cref="ExecutionPath.ParentExecutionPathId"/> references may dangle after
    /// pruning; the engine never reads them.</item>
    /// <item><b>Diagnostics:</b> capped to the most recent <see cref="DiagnosticsCap"/>; they are
    /// audit-only and never read by the engine.</item>
    /// <item><b>Scopes are never pruned:</b> loop iteration numbering derives from the cumulative
    /// loop-iteration scope count per owner node (<see cref="ResolveTargetScope"/>); pruning would collapse
    /// the count and let a later iteration reuse an earlier iteration key.</item>
    /// </list>
    /// Pruning shapes only what is written — the schema is unchanged and readers accept unpruned pre-#382
    /// blobs (see the golden-fixture tests). The in-memory state within a callback is never pruned;
    /// <see cref="FlowchartExecutionState.Sequence"/> is not bumped because no logical mutation occurs.
    /// </summary>
    private static FlowchartExecutionState PruneForPersistence(FlowchartExecutionState state)
    {
        var arrivals = state.Arrivals.Where(arrival => arrival.Status == FlowchartArrivalStatus.Arrived).ToArray();

        var retainedPathIds = new HashSet<string>(StringComparer.Ordinal) { RootPathId };
        foreach (var child in state.ActiveChildren)
            retainedPathIds.Add(child.ExecutionPathId);
        foreach (var arrival in arrivals)
            retainedPathIds.Add(arrival.ExecutionPathId);

        var paths = state.ExecutionPaths
            .Where(path => path.Status != ExecutionPathStatus.Completed
                           || retainedPathIds.Contains(path.ExecutionPathId))
            .ToArray();

        var diagnostics = state.Diagnostics.Count <= DiagnosticsCap
            ? state.Diagnostics
            : state.Diagnostics.Skip(state.Diagnostics.Count - DiagnosticsCap).ToArray();

        return state with { Arrivals = arrivals, ExecutionPaths = paths, Diagnostics = diagnostics };
    }

    private async ValueTask SaveStateAsync(IRuntimeActivityExecutionContext context, FlowchartExecutionState state)
    {
        var metadata = context.ActivityExecutionState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[StateMetadataKey] = JsonSerializer.Serialize(PruneForPersistence(state), SerializerOptions);
        var updatedState = context.ActivityExecutionState with { Metadata = metadata };
        var occurredAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var checkpointId = $"checkpoint:{context.SchedulerWorkItem.WorkItemId}:flowchart-state";
        var commitId = $"commit:{context.SchedulerWorkItem.WorkItemId}:flowchart-state";
        var checkpointMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = context.SchedulerWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = context.SchedulerWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = FlowchartStatePersistenceReason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ExecutableArtifactId] = context.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = context.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = context.PinnedExecutable.ArtifactHash
        };
        var stateChangeMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = context.SchedulerWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CheckpointReason] = FlowchartStatePersistenceReason
        };
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            updatedState,
            checkpointId,
            occurredAt,
            metadata: stateChangeMetadata,
            cancellationToken: context.CancellationToken);
        var commit = new RuntimeCheckpointCommit(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityInspectionCaptured,
                WorkflowExecutionId: context.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [updatedState.Execution.ActivityExecutionId],
                Metadata: checkpointMetadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: updatedState.Execution.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: updatedState,
                        Metadata: stateChangeMetadata)
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: updatedState.Execution.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: stateChangeMetadata)
                ]),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = context.SchedulerWorkItem.WorkItemId,
                [RuntimeMetadataKeys.CommandKind] = context.SchedulerWorkItem.CommandKind.ToString()
            });

        await checkpointCommitter.CommitAsync(commit, context.CancellationToken);
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
