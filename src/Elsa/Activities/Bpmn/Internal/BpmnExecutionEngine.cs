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

    /// <summary>The seam-A cancellation reason recorded on a losing event-based-gateway catch's cancelled subtree (spec 119).</summary>
    public const string EventGatewayRaceCancellationReason = "bpmn.event-based-gateway.superseded-by-first-catch";

    /// <summary>The seam-A reason recorded on a boundary catch listener's cancelled subtree when its host completed first (spec 120).</summary>
    public const string BoundarySupersededByHostCompletionReason = "bpmn.boundary.superseded-by-host-completion";

    /// <summary>The seam-A reason recorded on the host's (and sibling listeners') cancelled subtrees when an interrupting boundary — catch or error — fired (spec 120).</summary>
    public const string BoundaryHostInterruptedReason = "bpmn.boundary.host-interrupted";

    /// <summary>The seam-B fault-absorption reason recorded on the resolved incident when an error boundary absorbed the host's child fault (spec 120).</summary>
    public const string ErrorBoundaryAbsorptionReason = "bpmn.boundary.error-absorbed";

    public ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var graph = BpmnGraph.From(context.ExecutableNode);
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

        var graph = BpmnGraph.From(context.ExecutableNode);
        var state = BpmnStatePersister.LoadState(context.ActivityExecutionState) ?? BpmnStatePersister.CreateInitialState();

        var tokenId = ResolveTokenId(context, state, completionContext.CompletedChildExecutableNodeId);
        state = RemoveActiveChild(state, tokenId);
        var token = GetRequiredToken(state, tokenId);

        if (state.Terminated || token.Status == BpmnTokenStatus.Canceled)
        {
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Canceled, token.AtElementId, token.FlowId, token.TokenId, $"BPMN ignored completion for canceled token '{token.TokenId}'.");
            return ValueTask.FromResult(FinishEvaluation(context, new EvaluationResult(state)));
        }

        // The seam-A subtree cancellations this evaluation carries (staged only on a clean continuation).
        var pendingCancellations = new List<BpmnPendingSubtreeCancellation>();
        var liveChildAeiByNode = BuildLiveChildAeiByNode(context);

        // spec 119 D2/D3: if the completing token is a live event-based-gateway race member, it is the winner.
        // Resolve the race logically first (cancel losing member tokens, drop their active children), and carry
        // the losers' seam-A subtree cancellations — they are staged later only on a non-fault continuation.
        var race = state.Races.FirstOrDefault(candidate => !candidate.Resolved && candidate.MemberTokenIds.Contains(token.TokenId, StringComparer.Ordinal));
        if (race is not null)
        {
            state = ResolveEventRace(state, race, token.TokenId, liveChildAeiByNode, pendingCancellations);
            token = GetRequiredToken(state, token.TokenId);
        }

        // spec 120 D4: apply boundary-event completion semantics before dispatching the completing element's
        // behavior — a host completion tears down its live listeners, an interrupting listener tears down its
        // host and sibling listeners, a non-interrupting listener leaves both untouched. Purely logical here;
        // the seam-A cancellations are carried and staged only on a clean continuation.
        state = ApplyBoundaryCompletionSemantics(graph, state, token, liveChildAeiByNode, pendingCancellations);

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

        return ValueTask.FromResult(FinishEvaluation(context, result with { PendingSubtreeCancellations = pendingCancellations }));
    }

    /// <summary>
    /// Handles a terminal fault of one BPMN child. When the faulted child's host has an attached error
    /// boundary (spec 120 D5), the composite absorbs the fault through the spec 115 seam-B
    /// <c>RequestChildFaultAbsorption</c> — the named incident resolves, the faulted child's subtree is
    /// reclaimed, the host token and sibling catch listeners are cancelled, and the error boundary's outbound
    /// flows route — instead of faulting. Otherwise a faulted child cannot complete, so its token — and any
    /// downstream join that requires it — can never proceed, and the composite faults deterministically
    /// (Flowchart #308 parity).
    /// </summary>
    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext faultContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faultContext);

        var graph = BpmnGraph.From(context.ExecutableNode);
        var state = BpmnStatePersister.LoadState(context.ActivityExecutionState) ?? BpmnStatePersister.CreateInitialState();

        var faultedNodeId = faultContext.FaultedChildExecutableNodeId;
        var faultedTokenId = context.SchedulerWorkItem.CommandMetadata.TryGetValue(TokenIdMetadataKey, out var metadataTokenId) && !string.IsNullOrWhiteSpace(metadataTokenId)
            ? metadataTokenId
            : null;
        var faultedChild = faultedTokenId is not null
            ? state.ActiveChildren.FirstOrDefault(child => StringComparer.Ordinal.Equals(child.TokenId, faultedTokenId))
            : state.ActiveChildren.FirstOrDefault(child => StringComparer.Ordinal.Equals(child.NodeId, faultedNodeId));

        // spec 120 D5: absorb the fault through an error boundary attached to the faulted child's host.
        if (faultedChild is not null
            && faultContext.IncidentId is { } incidentId
            && graph.AttachedErrorBoundary(faultedChild.ElementId) is { } errorBoundary
            && state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, faultedChild.TokenId)) is { } hostToken
            && hostToken.Status is not (BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled))
        {
            return ValueTask.FromResult(AbsorbChildFaultThroughErrorBoundary(context, graph, state, faultedChild, hostToken, errorBoundary, incidentId, faultContext.FaultedChildActivityExecutionId));
        }

        if (faultedChild is not null)
            state = RemoveActiveChild(state, faultedChild.TokenId);

        var message = $"BPMN process faulted because child node '{faultedNodeId}' faulted: a faulted child cannot complete, so its token — and any downstream join that requires it — can no longer proceed.";
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, faultedChild?.ElementId, null, faultedChild?.TokenId, message);
        return ValueTask.FromResult(persister.StageState(
            RuntimeStructuralContinuation.Faulted(new ActivityFault("bpmn.child.faulted", message)),
            state));
    }

    /// <summary>
    /// Absorbs the host's child fault through its error boundary (spec 120 D5): drops the faulted child's
    /// active-child record (its subtree reclaim rides the seam-B absorption plan, so no seam-A cancellation is
    /// staged for it), flips the host token to <c>Canceled</c>, cancels the host's sibling catch listeners
    /// (seam-A carry), mints an <c>Active</c> token at the error boundary to route its outbound flows, and
    /// finishes through the shared quiescence machinery. The absorption request and the sibling-listener
    /// cancellations are staged only at the clean (Defer/Complete) exit — never on a routing fault.
    /// </summary>
    private RuntimeStructuralContinuation AbsorbChildFaultThroughErrorBoundary(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnActiveChild faultedChild,
        BpmnToken hostToken,
        BpmnElement errorBoundary,
        string incidentId,
        string faultedChildActivityExecutionId)
    {
        var pendingCancellations = new List<BpmnPendingSubtreeCancellation>();
        var liveChildAeiByNode = BuildLiveChildAeiByNode(context);

        // The faulted child's own subtree is reclaimed by the seam-B absorption plan; only drop its record.
        state = RemoveActiveChild(state, faultedChild.TokenId);
        state = UpdateToken(state, hostToken with { Status = BpmnTokenStatus.Canceled });
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Canceled, hostToken.AtElementId, hostToken.FlowId, hostToken.TokenId,
            $"BPMN error boundary '{errorBoundary.ElementId}' absorbed a fault of host '{hostToken.AtElementId}'; the host token was cancelled.");

        // Cancel the host's still-armed sibling catch listeners (their late completions are absorbed).
        state = CancelHostListeners(graph, state, hostToken.TokenId, listenerTokenToSkip: null, BoundaryHostInterruptedReason, liveChildAeiByNode, pendingCancellations);

        // Mint an active token at the error boundary so its behavior routes the error path.
        var errorToken = NewToken(state, errorBoundary.ElementId, flowId: null, parentTokenId: hostToken.TokenId, BpmnTokenStatus.Active, producingActivityExecutionId: faultedChildActivityExecutionId);
        state = AddToken(state, errorToken);
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.TokenEmitted, errorBoundary.ElementId, null, errorToken.TokenId,
            $"BPMN error boundary '{errorBoundary.ElementId}' fired and emitted token '{errorToken.TokenId}' to route the error path.");

        var result = Propagate(context, graph, state, faultedChildActivityExecutionId) with
        {
            PendingSubtreeCancellations = pendingCancellations,
            FaultAbsorption = new BpmnFaultAbsorption(incidentId, ErrorBoundaryAbsorptionReason)
        };

        return FinishEvaluation(context, result);
    }

    private sealed record EvaluationResult(
        BpmnExecutionState State,
        ActivityFault? Fault = null,
        bool Terminated = false,
        IReadOnlyCollection<BpmnPendingSubtreeCancellation>? PendingSubtreeCancellations = null,
        BpmnFaultAbsorption? FaultAbsorption = null);

    /// <summary>
    /// One live armed child subtree to be torn down via seam A (spec 119 event-based-gateway losers and spec
    /// 120 boundary host/listener teardown), carried on the evaluation result and staged only on a clean
    /// (non-fault) continuation. <see cref="Reason"/> is the per-cancellation seam-A reason.
    /// </summary>
    private sealed record BpmnPendingSubtreeCancellation(string ActivityExecutionId, string ElementId, string Reason);

    /// <summary>The seam-B fault absorption an error boundary staged for a child-fault evaluation (spec 120); staged only on a clean (non-fault) continuation.</summary>
    private sealed record BpmnFaultAbsorption(string IncidentId, string Reason);

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

                    // spec 119: the tokens an event-based gateway mints are the members of a first-catch-wins race.
                    var isEventBasedGateway = StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.EventBasedGateway);
                    var memberTokenIds = isEventBasedGateway ? new List<string>(command.FlowIds.Count) : null;

                    foreach (var flowId in command.FlowIds)
                    {
                        var flow = graph.GetRequiredFlow(flowId);
                        if (!StringComparer.Ordinal.Equals(flow.SourceRef, element.ElementId))
                            throw new BpmnExecutionException($"BPMN behavior for element '{element.ElementId}' emitted a token on flow '{flowId}', which does not originate from it.");

                        var status = BpmnTokenCoordinator.ShouldWaitAtJoin(graph, flow.TargetRef) ? BpmnTokenStatus.WaitingAtJoin : BpmnTokenStatus.Active;
                        var emitted = NewToken(state, flow.TargetRef, flow.FlowId, token.TokenId, status, schedulingActivityExecutionId);
                        state = AddToken(state, emitted);
                        memberTokenIds?.Add(emitted.TokenId);
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

                    if (memberTokenIds is { Count: > 0 })
                        state = AddRace(state, element.ElementId, memberTokenIds);

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
                    // spec 120 D3: arm the host's catch boundaries alongside its bound child (error boundaries stay dormant).
                    state = ArmCatchBoundaries(context, graph, state, element, token.TokenId, schedulingActivityExecutionId);
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
            // Clean completion — legal to stage the carried seam-A/seam-B teardown in this commit (spec 119 D3 / spec 120 D5).
            StagePendingSubtreeCancellations(context, result);
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

        // Clean deferral — legal to stage the carried seam-A/seam-B teardown in this commit (spec 119 D3 / spec 120 D5).
        StagePendingSubtreeCancellations(context, result);
        return persister.StageState(RuntimeStructuralContinuation.Defer, state);
    }

    /// <summary>
    /// Resolves a first-catch-wins race (spec 119): flips every losing member token to <c>Canceled</c>, drops
    /// each loser's active-child record, marks the race resolved, and appends each loser whose armed child is a
    /// live direct child of the process to <paramref name="pendingCancellations"/> so their runtime subtrees can
    /// be torn down via seam A on a clean continuation (D3). A loser whose child is not live is a benign skip
    /// (the token is still canceled, which absorbs its late completion). Purely logical — no context mutation.
    /// </summary>
    private static BpmnExecutionState ResolveEventRace(
        BpmnExecutionState state,
        BpmnEventRace race,
        string winnerTokenId,
        IReadOnlyDictionary<string, string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations)
    {
        foreach (var loserTokenId in race.MemberTokenIds.Where(id => !StringComparer.Ordinal.Equals(id, winnerTokenId)))
            state = CancelTokenAndChild(state, loserTokenId, EventGatewayRaceCancellationReason, liveChildAeiByNode, pendingCancellations,
                (token, reason) => $"BPMN event-based gateway '{race.GatewayElementId}' cancelled losing race member token '{token.TokenId}' at '{token.AtElementId}' after the first catch won.");

        return MarkRaceResolved(state, race.RaceId);
    }

    /// <summary>
    /// Applies spec 120 boundary-event completion semantics for the completing token, before the completing
    /// element's behavior is dispatched. A completing HOST token tears down its still-armed catch listeners; an
    /// INTERRUPTING catch-listener completion tears down its host token, the host's bound child, and the host's
    /// sibling listeners; a NON-INTERRUPTING listener completion leaves everything else running. Seam-A
    /// cancellations are appended to <paramref name="pendingCancellations"/> for staging on a clean continuation.
    /// </summary>
    private static BpmnExecutionState ApplyBoundaryCompletionSemantics(
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken completingToken,
        IReadOnlyDictionary<string, string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations)
    {
        var completingElement = graph.GetRequiredElement(completingToken.AtElementId);

        // Case A — the completing token is a boundary catch listener.
        if (StringComparer.Ordinal.Equals(completingElement.ElementType, BpmnElementTypes.BoundaryEvent))
        {
            if (!completingElement.CancelActivity || completingToken.ParentTokenId is not { } hostTokenId)
                return state; // non-interrupting (or a detached listener): host and siblings untouched.

            var hostToken = state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, hostTokenId));
            if (hostToken is null || hostToken.Status is BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled)
                return state; // the host already routed/was cancelled — nothing to interrupt.

            // Cancel the host token + its bound child, then the host's other listeners (skip the firing one).
            state = CancelTokenAndChild(state, hostTokenId, BoundaryHostInterruptedReason, liveChildAeiByNode, pendingCancellations,
                (token, reason) => $"BPMN interrupting boundary '{completingElement.ElementId}' cancelled host token '{token.TokenId}' at '{token.AtElementId}'.");
            return CancelHostListeners(graph, state, hostTokenId, listenerTokenToSkip: completingToken.TokenId, BoundaryHostInterruptedReason, liveChildAeiByNode, pendingCancellations);
        }

        // Case B — the completing token is a boundary host: tear down its still-armed catch listeners.
        if (graph.AttachedCatchBoundaries(completingElement.ElementId).Count == 0)
            return state;

        return CancelHostListeners(graph, state, completingToken.TokenId, listenerTokenToSkip: null, BoundarySupersededByHostCompletionReason, liveChildAeiByNode, pendingCancellations);
    }

    /// <summary>Arms the host's catch boundaries (spec 120 D3): one <c>AwaitingChild</c> listener token per catch boundary (parented to the host token), each scheduling its listener child. Error boundaries arm nothing.</summary>
    private static BpmnExecutionState ArmCatchBoundaries(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnElement hostElement,
        string hostTokenId,
        string schedulingActivityExecutionId)
    {
        foreach (var boundary in graph.AttachedCatchBoundaries(hostElement.ElementId))
        {
            if (boundary.ChildNodeId is not { } listenerNodeId)
                continue; // validation guarantees a catch boundary binds a listener; defensive.

            var listenerToken = NewToken(state, boundary.ElementId, flowId: null, parentTokenId: hostTokenId, BpmnTokenStatus.AwaitingChild, producingActivityExecutionId: schedulingActivityExecutionId);
            state = AddToken(state, listenerToken);
            state = BpmnScheduler.ScheduleChild(context, state, listenerNodeId, boundary.ElementId, listenerToken.TokenId, schedulingActivityExecutionId, "boundary:catch");
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Scheduled, boundary.ElementId, null, listenerToken.TokenId,
                $"BPMN boundary event '{boundary.ElementId}' armed listener child '{listenerNodeId}' alongside host '{hostElement.ElementId}'.");
        }

        return state;
    }

    /// <summary>Cancels the still-armed catch listeners parented to <paramref name="hostTokenId"/> (optionally skipping the one that just fired), carrying each live listener child's seam-A cancellation.</summary>
    private static BpmnExecutionState CancelHostListeners(
        BpmnGraph graph,
        BpmnExecutionState state,
        string hostTokenId,
        string? listenerTokenToSkip,
        string reason,
        IReadOnlyDictionary<string, string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations)
    {
        var listenerTokenIds = state.Tokens
            .Where(candidate =>
                candidate.ParentTokenId is { } parent && StringComparer.Ordinal.Equals(parent, hostTokenId) &&
                (listenerTokenToSkip is null || !StringComparer.Ordinal.Equals(candidate.TokenId, listenerTokenToSkip)) &&
                graph.GetRequiredElement(candidate.AtElementId) is { } element && StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent))
            .Select(candidate => candidate.TokenId)
            .ToArray();

        foreach (var listenerTokenId in listenerTokenIds)
            state = CancelTokenAndChild(state, listenerTokenId, reason, liveChildAeiByNode, pendingCancellations,
                (token, cancellationReason) => $"BPMN boundary listener token '{token.TokenId}' at '{token.AtElementId}' was cancelled ({cancellationReason}).");

        return state;
    }

    /// <summary>
    /// Flips one live token to <c>Canceled</c>, drops its active-child record, and — when its armed child is a
    /// live direct child of the process — appends a seam-A subtree cancellation with <paramref name="reason"/>.
    /// A token that is already terminal, or whose child is not live, is a benign skip. Shared by the spec 119
    /// race and the spec 120 boundary teardown.
    /// </summary>
    private static BpmnExecutionState CancelTokenAndChild(
        BpmnExecutionState state,
        string tokenId,
        string reason,
        IReadOnlyDictionary<string, string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations,
        Func<BpmnToken, string, string> diagnosticMessage)
    {
        var token = state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, tokenId));
        if (token is null || token.Status is BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled)
            return state;

        var child = state.ActiveChildren.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, tokenId));
        state = UpdateToken(state, token with { Status = BpmnTokenStatus.Canceled });
        if (child is not null)
        {
            state = RemoveActiveChild(state, tokenId);
            if (liveChildAeiByNode.TryGetValue(child.NodeId, out var activityExecutionId))
                pendingCancellations.Add(new BpmnPendingSubtreeCancellation(activityExecutionId, token.AtElementId, reason));
        }

        return BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Canceled, token.AtElementId, token.FlowId, token.TokenId, diagnosticMessage(token, reason));
    }

    /// <summary>The live direct child executions of this process keyed by executable node id (spec 119 D4); node ids are unique among a process's live direct children (validation-enforced).</summary>
    private static IReadOnlyDictionary<string, string> BuildLiveChildAeiByNode(IRuntimeActivityExecutionContext context)
    {
        var liveChildAeiByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var live in context.GetLiveChildActivities())
            liveChildAeiByNode[live.ExecutableNodeId] = live.ActivityExecutionId;
        return liveChildAeiByNode;
    }

    /// <summary>Stages each carried seam-A subtree cancellation and the seam-B fault absorption on the runtime context. Only called from a clean (non-fault) continuation (spec 119 D3 / spec 120 D5).</summary>
    private static void StagePendingSubtreeCancellations(IRuntimeActivityExecutionContext context, EvaluationResult result)
    {
        foreach (var cancellation in result.PendingSubtreeCancellations ?? [])
            context.RequestChildSubtreeCancellation(
                cancellation.ActivityExecutionId,
                cancellation.Reason,
                new Dictionary<string, string> { [ElementIdMetadataKey] = cancellation.ElementId });

        if (result.FaultAbsorption is { } absorption)
            context.RequestChildFaultAbsorption(absorption.IncidentId, absorption.Reason);
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
