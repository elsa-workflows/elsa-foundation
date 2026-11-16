using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using static Elsa.Activities.Flowchart.Internal.FlowchartScheduler;
using static Elsa.Activities.Flowchart.Internal.FlowchartScopeResolver;
using static Elsa.Activities.Flowchart.Internal.FlowchartStateMutator;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Translates a <see cref="FlowchartPolicyDecision"/> into state mutations and scheduling — the
/// <c>FlowchartPathScheduler</c> named in #275. Materializes first-wins race scopes, applies each policy
/// command (schedule node, write diagnostic, complete/wait/cancel path), resolves the target scope and
/// connection for a scheduled node (including loop-iteration scopes on backward edges), and hands off to the
/// join coordinator or the scheduler. Mechanical extraction of the former engine helpers; behavior and
/// ordering are unchanged.
/// </summary>
public sealed class FlowchartPolicyApplier(FlowchartReachabilityAnalyzer reachabilityAnalyzer, FlowchartJoinCoordinator joinCoordinator)
{
    public FlowchartExecutionState ApplyDecision(
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
        var takenConnectionIds = new HashSet<string>(StringComparer.Ordinal);
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
                FlowchartPolicyCommandKind.ScheduleNode => ApplyScheduleNodeCommand(context, state, graph, effectiveCommand, currentPath, schedulingActivityExecutionId, policyKind, scheduledNodes, takenConnectionIds),
                FlowchartPolicyCommandKind.WriteDiagnostic => ApplyDiagnosticCommand(state, effectiveCommand, currentPath),
                FlowchartPolicyCommandKind.CompleteExecutionPath => UpdatePath(state, currentPath with { Status = ExecutionPathStatus.Completed }),
                FlowchartPolicyCommandKind.WaitExecutionPath => UpdatePath(state, currentPath with { Status = ExecutionPathStatus.Waiting }),
                FlowchartPolicyCommandKind.CancelExecutionPath => UpdatePath(state, currentPath with { Status = ExecutionPathStatus.Canceled }),
                _ => throw NewInvalidPolicyCommand(policyKind, $"Policy command '{effectiveCommand.Kind}' is not supported by this Flowchart execution engine slice.")
            };
        }

        // A policy decides which of its node's outbound connections to take, so the ones it left out are the
        // untaken half of this completion, exactly as an unmatched outcome port is on the routing path. Dead
        // arrivals land in the same scope the schedule commands used, so live and dead answers to one join
        // share a partition key.
        //
        // Only when the policy actually routed. A decision that schedules nothing — one that parks the path
        // with WaitExecutionPath, or cancels it — has not answered the routing question at all, and reading its
        // silence as "took none of them" would declare every branch behind that node dead on the strength of a
        // decision the policy has not made yet.
        if (currentPath.CurrentNodeId is not { } sourceNodeId || scheduledNodes.Count == 0)
            return state;

        var deadPathScopeId = firstWinsScopeId ?? defaultScheduleScopeId ?? currentPath.ExecutionScopeId;
        return joinCoordinator.RouteUntakenOutbound(
            context,
            state,
            graph,
            sourceNodeId,
            deadPathScopeId,
            ResolveIterationKey(state, deadPathScopeId),
            takenConnectionIds,
            schedulingActivityExecutionId);
    }

    private FlowchartExecutionState ApplyScheduleNodeCommand(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        FlowchartPolicyCommand command,
        ExecutionPath currentPath,
        string schedulingActivityExecutionId,
        string policyKind,
        HashSet<string> scheduledNodes,
        HashSet<string> takenConnectionIds)
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

        var iterationKey = ResolveIterationKey(state, executionScopeId);
        var connection = ResolveScheduleConnection(graph, command, currentPath, nodeId, policyKind);
        if (connection is not null)
            takenConnectionIds.Add(graph.GetConnectionId(connection));
        if (connection is not null && reachabilityAnalyzer.IsBackwardEdge(graph, connection.Source.NodeId, connection.Target.NodeId))
        {
            var currentScope = state.Scopes.FirstOrDefault(scope => StringComparer.Ordinal.Equals(scope.ExecutionScopeId, currentPath.ExecutionScopeId))
                ?? throw new InvalidOperationException($"Execution scope '{currentPath.ExecutionScopeId}' not found. ActivityExecutionId='{context.ActivityExecutionState.Execution.ActivityExecutionId}'.");
            var loopScope = ResolveTargetScope(reachabilityAnalyzer, state, graph, currentScope, connection);
            if (state.Scopes.All(existing => !StringComparer.Ordinal.Equals(existing.ExecutionScopeId, loopScope.ExecutionScopeId)))
                state = AddScope(state, loopScope);
            executionScopeId = loopScope.ExecutionScopeId;
            iterationKey = ResolveIterationKey(state, executionScopeId);
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.LoopIteration, nodeId, graph.GetConnectionId(connection), currentPath.ExecutionPathId, executionScopeId, $"Flowchart created loop iteration scope '{executionScopeId}' for loopback to '{nodeId}'.");
        }

        if (connection is not null)
        {
            var connectionId = graph.GetConnectionId(connection);
            var arrivalPath = NewPath(state, currentPath.ExecutionPathId, executionScopeId, nodeId, connectionId, schedulingActivityExecutionId, ExecutionPathStatus.Active, iterationKey);
            state = AddPath(state, arrivalPath);
            state = AddArrival(state, arrivalPath, connection, connectionId, schedulingActivityExecutionId);

            if (joinCoordinator.ShouldWaitForTarget(state, graph, nodeId, executionScopeId, iterationKey))
            {
                state = UpdatePath(state, arrivalPath with { Status = ExecutionPathStatus.Waiting });
                return FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Waiting, nodeId, connectionId, arrivalPath.ExecutionPathId, executionScopeId, $"Flowchart policy '{policyKind}' is waiting at implicit join '{nodeId}'.");
            }

            return joinCoordinator.FireJoinOrContinuation(context, state, graph, nodeId, executionScopeId, iterationKey, schedulingActivityExecutionId, connectionId);
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
}
