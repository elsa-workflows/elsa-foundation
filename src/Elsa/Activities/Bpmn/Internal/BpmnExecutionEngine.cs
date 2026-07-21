using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using static Elsa.Activities.Bpmn.Internal.BpmnStateMutator;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Orchestrates BPMN composite execution across its three entry points (<see cref="StartAsync"/>,
/// <see cref="OnChildCompletedAsync"/>, <see cref="OnChildFaultedAsync"/>), mirroring
/// <c>FlowchartExecutionEngine</c>'s decomposition: the engine owns dispatch decisions and the token
/// propagation loop, and delegates join accounting to <see cref="BpmnTokenCoordinator"/>, behavior
/// resolution to <see cref="IBpmnBehaviorRegistry"/>, and state load/stage/prune to
/// <see cref="BpmnStatePersister"/>. All record ids remain a pure function of
/// <see cref="BpmnExecutionState.Sequence"/>, whose only mutation home is <see cref="BpmnStateMutator"/>.
/// </summary>
public sealed class BpmnExecutionEngine(
    IBpmnBehaviorRegistry behaviorRegistry,
    BpmnTokenCoordinator tokenCoordinator,
    BpmnStatePersister persister)
{
    public const string StateTypeAlias = "Elsa.Bpmn.ExecutionState";
    public const int StateSchemaVersion = 1;
    public const string ParentActivityExecutionIdMetadataKey = "bpmn.parentActivityExecutionId";
    public const string TokenIdMetadataKey = "bpmn.tokenId";
    public const string ElementIdMetadataKey = "bpmn.elementId";
    public const string SchedulingCauseMetadataKey = "bpmn.schedulingCause";
    public const string TargetNodeIdMetadataKey = "bpmn.targetNodeId";

    public ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var graph = context.ExecutableNode.GetOrAddRoutingStructure(BpmnGraph.From);
        if (graph.Elements.Count == 0)
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        var state = BpmnStatePersister.LoadState(context.ActivityExecutionState) ?? BpmnStatePersister.CreateInitialState();

        // Spec 117 D6: a trigger delivery targets this composite (its own node id was the matched binding's node),
        // so exactly one event-defined start element — named by the forwarded binding metadata — seeds a token.
        // Otherwise (direct invocation) every none start event seeds a token and event-defined starts stay dormant.
        var seedResult = context.TriggerNodeId is { } triggerNodeId && StringComparer.Ordinal.Equals(triggerNodeId, context.ExecutableNode.ExecutableNodeId)
            ? SeedFromTrigger(context, graph, state)
            : SeedFromDirectInvocation(graph, state);

        if (seedResult.Fault is not null)
            return ValueTask.FromResult(FinishEvaluation(context, seedResult));

        var result = Propagate(context, graph, seedResult.State, context.ActivityExecutionState.Execution.ActivityExecutionId);
        return ValueTask.FromResult(FinishEvaluation(context, result));
    }

    private static EvaluationResult SeedFromTrigger(IRuntimeActivityExecutionContext context, BpmnGraph graph, BpmnExecutionState state)
    {
        if (context.TriggerMetadata is not { } metadata ||
            !metadata.TryGetValue(BpmnStartTrigger.StartElementIdMetadataKey, out var startElementId) ||
            string.IsNullOrWhiteSpace(startElementId))
        {
            const string message = "BPMN process was started by a trigger delivery but the matched trigger binding carried no start element id.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, null, null, null, message);
            return new EvaluationResult(state, new ActivityFault("bpmn.start.unresolved-trigger", message));
        }

        var startElement = graph.StartEvents.FirstOrDefault(element =>
            StringComparer.Ordinal.Equals(element.ElementId, startElementId) && element.EventDefinitions.Count > 0);
        if (startElement is null)
        {
            var message = $"BPMN process was started by a trigger delivery targeting start element '{startElementId}', which is not an event-defined start event of this process.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, null, null, null, message);
            return new EvaluationResult(state, new ActivityFault("bpmn.start.unresolved-trigger", message));
        }

        return SeedToken(state, startElement.ElementId, $"BPMN event-defined start event '{startElement.ElementId}' emitted the initial token from a trigger delivery.");
    }

    private static EvaluationResult SeedFromDirectInvocation(BpmnGraph graph, BpmnExecutionState state)
    {
        var noneStarts = graph.StartEvents.Where(element => element.EventDefinitions.Count == 0).ToArray();
        if (noneStarts.Length == 0)
        {
            const string message = "BPMN process has no none start event to start on direct invocation; its start events are all event-defined (message/signal/timer) and require a matching stimulus.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, null, null, null, message);
            return new EvaluationResult(state, new ActivityFault("bpmn.start.none-available", message));
        }

        foreach (var startEvent in noneStarts)
            state = SeedToken(state, startEvent.ElementId, $"BPMN start event '{startEvent.ElementId}' emitted the initial token.").State;

        return new EvaluationResult(state);
    }

    private static EvaluationResult SeedToken(BpmnExecutionState state, string elementId, string message)
    {
        var token = NewToken(state, elementId, flowId: null, parentTokenId: null, BpmnTokenStatus.Active, producingActivityExecutionId: null);
        state = AddToken(state, token);
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.TokenEmitted, elementId, null, token.TokenId, message);
        return new EvaluationResult(state);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completionContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completionContext);

        var graph = context.ExecutableNode.GetOrAddRoutingStructure(BpmnGraph.From);
        var state = BpmnStatePersister.LoadState(context.ActivityExecutionState) ?? BpmnStatePersister.CreateInitialState();

        var tokenId = ResolveTokenId(context, state, completionContext.CompletedChildExecutableNodeId);
        state = RemoveActiveChild(state, tokenId);
        var token = GetRequiredToken(state, tokenId);

        if (state.Terminated || token.Status == BpmnTokenStatus.Canceled)
        {
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Canceled, token.AtElementId, token.FlowId, token.TokenId, $"BPMN ignored completion for canceled token '{token.TokenId}'.");
            return ValueTask.FromResult(FinishEvaluation(context, new EvaluationResult(state)));
        }

        var element = graph.GetRequiredElement(token.AtElementId);
        var behavior = behaviorRegistry.GetRequired(BpmnElementFamilies.Resolve(element));
        var behaviorContext = new BpmnBehaviorContext(
            BpmnBehaviorTrigger.ChildCompleted,
            element,
            token,
            graph.OutboundFlows(element.ElementId),
            graph.InboundFlows(element.ElementId),
            completionContext.OutcomeNames,
            state);

        var result = ApplyDecision(context, graph, state, token, element, Execute(behavior, behaviorContext, element), completionContext.CompletedChildActivityExecutionId);
        if (result is { Fault: null, Terminated: false })
            result = Propagate(context, graph, result.State, completionContext.CompletedChildActivityExecutionId);

        return ValueTask.FromResult(FinishEvaluation(context, result));
    }

    /// <summary>
    /// Handles a terminal fault of one BPMN child. A faulted child cannot complete, so its token — and
    /// any downstream join that requires it — can never proceed. Rather than leave the process Running
    /// forever, the composite faults deterministically (surfacing a composite incident), mirroring the
    /// Flowchart engine's #308 behavior. Error boundary events replace this rule in the events tier.
    /// </summary>
    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext faultContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faultContext);

        var state = BpmnStatePersister.LoadState(context.ActivityExecutionState) ?? BpmnStatePersister.CreateInitialState();

        var faultedNodeId = faultContext.FaultedChildExecutableNodeId;
        var faultedChild = state.ActiveChildren.FirstOrDefault(child => StringComparer.Ordinal.Equals(child.NodeId, faultedNodeId));
        if (faultedChild is not null)
            state = RemoveActiveChild(state, faultedChild.TokenId);

        var message = $"BPMN process faulted because child node '{faultedNodeId}' faulted: a faulted child cannot complete, so its token — and any downstream join that requires it — can no longer proceed.";
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, faultedChild?.ElementId, null, faultedChild?.TokenId, message);
        return ValueTask.FromResult(persister.StageState(
            RuntimeStructuralContinuation.Faulted(new ActivityFault("bpmn.child.faulted", message)),
            state));
    }

    private sealed record EvaluationResult(BpmnExecutionState State, ActivityFault? Fault = null, bool Terminated = false);

    /// <summary>
    /// The token propagation loop: releases ready joins, then dispatches the first live
    /// <see cref="BpmnTokenStatus.Active"/> token to its element behavior, until the state is quiescent
    /// (every token consumed, parked at a join, or awaiting a scheduled child).
    /// </summary>
    private EvaluationResult Propagate(IRuntimeActivityExecutionContext context, BpmnGraph graph, BpmnExecutionState state, string schedulingActivityExecutionId)
    {
        while (true)
        {
            state = tokenCoordinator.ReleaseReadyJoins(state, graph);

            var token = state.Tokens.FirstOrDefault(candidate => candidate.Status == BpmnTokenStatus.Active);
            if (token is null)
                return new EvaluationResult(state);

            var element = graph.GetRequiredElement(token.AtElementId);
            var behavior = behaviorRegistry.GetRequired(BpmnElementFamilies.Resolve(element));
            var behaviorContext = new BpmnBehaviorContext(
                BpmnBehaviorTrigger.TokenArrived,
                element,
                token,
                graph.OutboundFlows(element.ElementId),
                graph.InboundFlows(element.ElementId),
                [],
                state);

            var result = ApplyDecision(context, graph, state, token, element, Execute(behavior, behaviorContext, element), schedulingActivityExecutionId);
            if (result.Fault is not null || result.Terminated)
                return result;

            state = result.State;
        }
    }

    private static BpmnBehaviorDecision Execute(IBpmnElementBehavior behavior, IBpmnBehaviorContext behaviorContext, BpmnElement element)
    {
        try
        {
            return behaviorContext.Trigger == BpmnBehaviorTrigger.TokenArrived
                ? behavior.OnTokenArrived(behaviorContext)
                : behavior.OnChildCompleted(behaviorContext);
        }
        catch (BpmnExecutionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new BpmnExecutionException($"BPMN behavior '{behavior.ElementFamily}' failed for element '{element.ElementId}'.", exception);
        }
    }

    /// <summary>
    /// Validates and applies one behavior decision. Mutation and scheduling authority stays here; the
    /// behaviors only describe what should happen.
    /// </summary>
    private EvaluationResult ApplyDecision(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken token,
        BpmnElement element,
        BpmnBehaviorDecision decision,
        string schedulingActivityExecutionId)
    {
        foreach (var command in decision.Commands)
        {
            switch (command.Kind)
            {
                case BpmnBehaviorCommandKind.EmitTokens:
                {
                    state = UpdateToken(state, token with { Status = BpmnTokenStatus.Consumed });
                    if (command.FlowIds.Count == 0)
                    {
                        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Consumed, element.ElementId, null, token.TokenId, $"BPMN token '{token.TokenId}' ended at element '{element.ElementId}' (no outbound sequence flow taken).");
                        break;
                    }

                    foreach (var flowId in command.FlowIds)
                    {
                        var flow = graph.GetRequiredFlow(flowId);
                        if (!StringComparer.Ordinal.Equals(flow.SourceRef, element.ElementId))
                            throw new BpmnExecutionException($"BPMN behavior for element '{element.ElementId}' emitted a token on flow '{flowId}', which does not originate from it.");

                        var status = BpmnTokenCoordinator.ShouldWaitAtJoin(graph, flow.TargetRef) ? BpmnTokenStatus.WaitingAtJoin : BpmnTokenStatus.Active;
                        var emitted = NewToken(state, flow.TargetRef, flow.FlowId, token.TokenId, status, schedulingActivityExecutionId);
                        state = AddToken(state, emitted);
                        state = BpmnDiagnosticAccumulator.Add(
                            state,
                            status == BpmnTokenStatus.WaitingAtJoin ? BpmnDiagnosticKind.Waiting : BpmnDiagnosticKind.TokenEmitted,
                            flow.TargetRef,
                            flow.FlowId,
                            emitted.TokenId,
                            status == BpmnTokenStatus.WaitingAtJoin
                                ? $"BPMN token '{emitted.TokenId}' is waiting at join '{flow.TargetRef}'."
                                : $"BPMN token '{emitted.TokenId}' arrived at element '{flow.TargetRef}' via flow '{flow.FlowId}'.");
                    }

                    break;
                }
                case BpmnBehaviorCommandKind.ScheduleChild:
                {
                    if (element.ChildNodeId is not { } childNodeId)
                        throw new BpmnExecutionException($"BPMN behavior for element '{element.ElementId}' requested a child schedule, but the element binds no child activity.");

                    graph.GetRequiredChildNode(childNodeId);
                    state = UpdateToken(state, token with { Status = BpmnTokenStatus.AwaitingChild });
                    state = BpmnScheduler.ScheduleChild(context, state, childNodeId, element.ElementId, token.TokenId, schedulingActivityExecutionId, $"element:{element.ElementType}");
                    state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Scheduled, element.ElementId, null, token.TokenId, $"BPMN element '{element.ElementId}' scheduled child activity '{childNodeId}'.");
                    break;
                }
                case BpmnBehaviorCommandKind.ConsumeToken:
                {
                    state = UpdateToken(state, token with { Status = BpmnTokenStatus.Consumed });
                    state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Consumed, element.ElementId, null, token.TokenId, $"BPMN end event '{element.ElementId}' consumed token '{token.TokenId}'.");
                    break;
                }
                case BpmnBehaviorCommandKind.TerminateProcess:
                {
                    state = UpdateToken(state, token with { Status = BpmnTokenStatus.Consumed });
                    // In-flight children keep running; their late completions are absorbed by the
                    // canceled-token guard, or ignored outright once the composite has completed
                    // (Flowchart Break parity).
                    state = CancelLiveWork(state);
                    state = state with { Terminated = true, Sequence = state.Sequence + 1 };
                    state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Terminated, element.ElementId, null, token.TokenId, command.Message ?? $"BPMN terminate end event '{element.ElementId}' ended the process.");
                    return new EvaluationResult(state, Terminated: true);
                }
                case BpmnBehaviorCommandKind.Fault:
                {
                    var faultCode = string.IsNullOrWhiteSpace(command.FaultCode) ? "bpmn.behavior.faulted" : command.FaultCode;
                    var message = command.Message ?? $"BPMN behavior for element '{element.ElementId}' faulted the process.";
                    state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, element.ElementId, null, token.TokenId, message);
                    return new EvaluationResult(state, new ActivityFault(faultCode, message));
                }
                default:
                    throw new BpmnExecutionException($"BPMN behavior command '{command.Kind}' is not supported by this engine slice.");
            }
        }

        return new EvaluationResult(state);
    }

    /// <summary>
    /// Stages the state and picks the continuation. The runtime forbids a terminal decision
    /// (Complete/Fault) in an evaluation that also staged child schedules, so terminate and behavior
    /// faults raised mid-propagation are deferred: the state carries the verdict
    /// (<see cref="BpmnExecutionState.Terminated"/> / <see cref="BpmnExecutionState.PendingFault"/>)
    /// and the next child completion surfaces it.
    /// </summary>
    private RuntimeStructuralContinuation FinishEvaluation(IRuntimeActivityExecutionContext context, EvaluationResult result)
    {
        var scheduledThisEvaluation = context.GetChildActivityScheduleRequests().Count > 0;
        var state = result.State;

        if (result.Fault is not null)
        {
            if (!scheduledThisEvaluation)
                return persister.StageState(RuntimeStructuralContinuation.Faulted(result.Fault), state);

            state = CancelLiveWork(state);
            state = state with { PendingFault = new BpmnPendingFault(result.Fault.Code, result.Fault.Message), Sequence = state.Sequence + 1 };
            return persister.StageState(RuntimeStructuralContinuation.Defer, state);
        }

        if (state.PendingFault is { } pendingFault && !scheduledThisEvaluation)
            return persister.StageState(RuntimeStructuralContinuation.Faulted(new ActivityFault(pendingFault.FaultCode, pendingFault.Message)), state);

        if (result.Terminated || state.Terminated)
        {
            return scheduledThisEvaluation
                ? persister.StageState(RuntimeStructuralContinuation.Defer, state)
                : persister.StageState(RuntimeStructuralContinuation.Complete(), state);
        }

        var liveTokens = state.Tokens.Where(token => token.Status is BpmnTokenStatus.Active or BpmnTokenStatus.AwaitingChild or BpmnTokenStatus.WaitingAtJoin).ToArray();
        if (liveTokens.Length == 0 && state.ActiveChildren.Count == 0)
        {
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Completed, null, null, null, "BPMN process completed because no live tokens remain.");
            return persister.StageState(RuntimeStructuralContinuation.Complete(), state);
        }

        // A parked join arrival with nothing live left to produce the missing arrivals can never fire
        // (e.g. a parallel join downstream of an exclusive decision). Fault deterministically instead of
        // leaving the process Running forever.
        if (state.ActiveChildren.Count == 0 && liveTokens.All(token => token.Status == BpmnTokenStatus.WaitingAtJoin))
        {
            var joinElementIds = string.Join(", ", liveTokens.Select(token => token.AtElementId).Distinct(StringComparer.Ordinal));
            var message = $"BPMN process deadlocked: token(s) wait at join(s) [{joinElementIds}] but no live token or running child can produce the missing arrival(s).";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, null, null, null, message);
            return persister.StageState(RuntimeStructuralContinuation.Faulted(new ActivityFault("bpmn.join.deadlock", message)), state);
        }

        return persister.StageState(RuntimeStructuralContinuation.Defer, state);
    }

    /// <summary>Cancels every live token and drops the active-child records; in-flight children keep running and their late completions are absorbed by the token-status guard.</summary>
    private static BpmnExecutionState CancelLiveWork(BpmnExecutionState state)
    {
        foreach (var liveToken in state.Tokens.Where(candidate => candidate.Status is BpmnTokenStatus.Active or BpmnTokenStatus.AwaitingChild or BpmnTokenStatus.WaitingAtJoin).ToArray())
            state = UpdateToken(state, liveToken with { Status = BpmnTokenStatus.Canceled });

        return state with { ActiveChildren = [], Sequence = state.Sequence + 1 };
    }

    private static string ResolveTokenId(IRuntimeActivityExecutionContext context, BpmnExecutionState state, string completedNodeId)
    {
        if (context.SchedulerWorkItem.CommandMetadata.TryGetValue(TokenIdMetadataKey, out var metadataTokenId) && !string.IsNullOrWhiteSpace(metadataTokenId))
            return metadataTokenId;

        var candidates = state.ActiveChildren
            .Where(child => StringComparer.Ordinal.Equals(child.NodeId, completedNodeId))
            .ToArray();

        if (candidates.Length == 1)
            return candidates[0].TokenId;

        throw new BpmnExecutionException($"Unable to resolve the BPMN token for completed child node '{completedNodeId}'.");
    }
}
