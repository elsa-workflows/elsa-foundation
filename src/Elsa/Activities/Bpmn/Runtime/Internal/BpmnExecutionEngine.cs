using System.Text.Json;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal.Behaviors;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
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

    /// <summary>The scheduling cause recorded on a multi-instance loop's instance child schedule (spec 121).</summary>
    public const string MultiInstanceSchedulingCause = "multi-instance";

    /// <summary>The scheduling cause recorded on a compensation handler's bound child schedule (spec 124).</summary>
    public const string CompensationSchedulingCause = "compensation";

    /// <summary>The seam-A reason recorded on a compensation run's cancelled handler sub-tree when its coordinator throw token was cancelled mid-replay (spec 124).</summary>
    public const string CompensationRunCancelledReason = "bpmn.compensation.run-cancelled";

    /// <summary>The reason recorded on the other live tokens a cancel end event stops when it begins cancelling a transaction scope (spec 125).</summary>
    public const string TransactionCancelledStopReason = "bpmn.transaction.cancel-stopped-live-work";

    /// <summary>The distinguishable outcome a cancelled transaction completes with (spec 125); the parent maps it to the attached cancel boundary, and a root transaction completes the workflow with it.</summary>
    public const string CancelledOutcomeName = "Cancelled";

    /// <summary>The single reserved seam-C notification code the module raises for every escalation (spec 127); the escalation identity travels in the payload, keeping the seam-C code namespace clean.</summary>
    public const string EscalationNotificationCode = "bpmn.escalation";

    /// <summary>The seam-A reason recorded on the host's (and its subtree's) cancelled tokens when an interrupting escalation boundary fired (spec 127).</summary>
    public const string EscalationHostInterruptedReason = "bpmn.escalation.host-interrupted";

    /// <summary>The scheduling cause recorded on an event-subprocess body's child schedule (spec 128); gates the third <see cref="StartAsync"/> seeding path so an inherited start-element hint never contaminates an ordinary nested process.</summary>
    public const string EventSubprocessBodySchedulingCause = "bpmn.event-subprocess.body";

    /// <summary>The reason recorded on the other live tokens an interrupting event subprocess stops when it interrupts its scope (spec 128).</summary>
    public const string EventSubprocessScopeInterruptedReason = "bpmn.event-subprocess.scope-interrupted";

    /// <summary>The seam-B fault-absorption reason recorded on the resolved incident when an error event subprocess absorbed the host's child fault (spec 128).</summary>
    public const string EventSubprocessErrorAbsorptionReason = "bpmn.event-subprocess.error-absorbed";

    /// <summary>The scheduling cause recorded on a tier-2 scope listener's suspending child schedule (spec 134); armed at scope start and re-armed per non-interrupting fire.</summary>
    public const string ScopeListenerSchedulingCause = "bpmn.event-subprocess.listener";

    /// <summary>The seam-A reason recorded on a still-armed scope listener's cancelled durable child when its scope completed and tore the listener down (spec 134 teardown-then-complete).</summary>
    public const string EventSubprocessListenerSupersededByCompletionReason = "bpmn.event-subprocess.listener-superseded-by-completion";

    /// <summary>The deterministic fault code raised when a transaction child completes Cancelled but no cancel boundary is attached to route the cancellation (spec 125 D2b).</summary>
    public const string TransactionCancelledUnhandledFaultCode = "bpmn.transaction.cancelled-unhandled";

    /// <summary>The seam-A reason recorded on the host's (and sibling listeners') cancelled subtrees when a call activity's failure outcome routed a catcher or faulted (spec 133 D3).</summary>
    public const string CallActivityFailureRoutedReason = "bpmn.call-activity.failure-routed";

    /// <summary>The <c>Faulted</c> failure outcome of a call activity's bound <c>DispatchWorkflow</c> child (spec 133 D3). Mirrors <c>DispatchWorkflowOutcomes.Faulted</c> by convention; kept local so the module takes no runtime dependency.</summary>
    public const string CallActivityFaultedOutcomeName = "Faulted";

    /// <summary>The <c>DispatchFailed</c> failure outcome of a call activity's bound <c>DispatchWorkflow</c> child (spec 133 D3). Mirrors <c>DispatchWorkflowOutcomes.DispatchFailed</c> by convention.</summary>
    public const string CallActivityDispatchFailedOutcomeName = "DispatchFailed";

    /// <summary>The <c>Cancelled</c> failure outcome of a call activity's bound <c>DispatchWorkflow</c> child (spec 133 D3). Shares the value of <see cref="CancelledOutcomeName"/> by convention.</summary>
    public const string CallActivityCancelledOutcomeName = "Cancelled";

    /// <summary>The deterministic composite fault code raised when a call activity's <c>Faulted</c> outcome reaches no error catcher (spec 133 D3).</summary>
    public const string CallActivityFaultedFaultCode = "bpmn.call-activity.faulted";

    /// <summary>The deterministic composite fault code raised when a call activity's <c>DispatchFailed</c> outcome reaches no error catcher (spec 133 D3).</summary>
    public const string CallActivityDispatchFailedFaultCode = "bpmn.call-activity.dispatch-failed";

    /// <summary>The deterministic composite fault code raised when a call activity's <c>Cancelled</c> outcome reaches no error catcher (spec 133 D3).</summary>
    public const string CallActivityCancelledFaultCode = "bpmn.call-activity.cancelled";

    /// <summary>The call-activity failure outcomes the engine translates into BPMN error handling (spec 133 D3), in deterministic precedence order. <c>Completed</c>/<c>Dispatched</c> are untouched (normal task-flow routing).</summary>
    private static readonly IReadOnlyList<string> CallActivityFailureOutcomes =
        [CallActivityFaultedOutcomeName, CallActivityDispatchFailedOutcomeName, CallActivityCancelledOutcomeName];

    /// <summary>An empty live-child map: a cancel end stops other live work logically only (in-flight children keep running and are absorbed on late completion — the terminate precedent), so no seam-A subtree cancellation is staged.</summary>
    private static readonly IReadOnlyDictionary<(string NodeId, string? IterationId), string> NoLiveChildren =
        new Dictionary<(string NodeId, string? IterationId), string>();

    /// <summary>The prefix of a multi-instance instance's minted iteration id (spec 121); the instance token id is Sequence-derived and unique across the process.</summary>
    public const string MultiInstanceIterationIdPrefix = "bpmn-mi:";

    /// <summary>The deterministic fault code raised when a collection-mode multi-instance host's collection variable is not visible/declared (spec 123 D2; defensive — unreachable post-validation for a process's own variable).</summary>
    public const string CollectionUnreadableFaultCode = "bpmn.loop.collection-unreadable";

    /// <summary>The deterministic fault code raised when a collection-mode multi-instance host's collection variable holds a present, non-array value (spec 123 D2).</summary>
    public const string CollectionNotACollectionFaultCode = "bpmn.loop.collection-not-a-collection";

    /// <summary>The deterministic fault code raised when a collection-mode multi-instance host's collection variable holds an externally-stored (non-inline) payload (spec 123 D2 stated cut).</summary>
    public const string CollectionNotInlineFaultCode = "bpmn.loop.collection-not-inline";

    public ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var graph = context.ExecutableNode.GetOrAddRoutingStructure(BpmnGraph.From);
        if (graph.Elements.Count == 0)
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        var state = BpmnStatePersister.LoadState(context.ActivityExecutionState) ?? BpmnStatePersister.CreateInitialState();

        // Spec 117 D6: a trigger delivery targets this composite (its own node id was the matched binding's node),
        // so exactly one event-defined start element — named by the forwarded binding metadata — seeds a token.
        // spec 128 D2: an event-subprocess body carries a scheduled-start hint (gated on its scheduling cause), so
        // exactly its single event-start element seeds. Otherwise (direct invocation) every none start seeds a token.
        var seedResult = context.TriggerNodeId is { } triggerNodeId && StringComparer.Ordinal.Equals(triggerNodeId, context.ExecutableNode.ExecutableNodeId)
            ? SeedFromTrigger(context, graph, state)
            : TryReadScheduledStartHint(context) is { } scheduledStartElementId
                ? SeedFromScheduledStart(graph, state, scheduledStartElementId)
                : SeedFromDirectInvocation(graph, state);

        if (seedResult.Fault is not null)
            return ValueTask.FromResult(FinishEvaluation(context, seedResult));

        // spec 134 D2: arm one scope listener per external-trigger (message/signal/timer) event subprocess at this
        // seeding path (deterministic catcher element-id ordinal). Arming is TWO-PHASE (a Deviation the runtime forces):
        // the listener TOKENS are minted BEFORE propagating the seed — so an interrupting activation raised by the
        // seed's own propagation (an own-scope escalation) drains them as ordinary live tokens — but their suspending
        // children are scheduled AFTER propagation and only when real work remains live (an active child is pending).
        // The runtime forbids scheduling children in a terminal evaluation, so a scope whose seed completes
        // synchronously (start → end) would otherwise strand just-scheduled listener children against the same
        // evaluation's completion; deferring the child schedule lets such a scope complete with the listener token torn
        // down and no child ever scheduled (BPMN semantics: the listener never got a chance to fire). Every StartAsync
        // seeding path arms — a nested event-subprocess body scheduled via the D2 hint arms its OWN nested listeners.
        var ownerActivityExecutionId = context.ActivityExecutionState.Execution.ActivityExecutionId;
        var armedState = MintScopeListenerTokens(graph, seedResult.State, ownerActivityExecutionId);

        var result = Propagate(context, graph, armedState, ownerActivityExecutionId);
        if (result is { Fault: null, Terminated: false } && result.State.ActiveChildren.Count > 0)
            result = result with { State = ScheduleScopeListenerChildren(context, graph, result.State, ownerActivityExecutionId) };

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

    /// <summary>
    /// Reads the event-subprocess body start-element hint from the scheduler work item's command metadata (spec 128
    /// D2), gated on the event-subprocess-body scheduling cause so an inherited hint on an ordinary nested process's
    /// schedule (whose cause differs) is ignored. <c>null</c> when absent — the byte-identical no-hint path.
    /// </summary>
    private static string? TryReadScheduledStartHint(IRuntimeActivityExecutionContext context)
    {
        var metadata = context.SchedulerWorkItem.CommandMetadata;
        return metadata.TryGetValue(SchedulingCauseMetadataKey, out var cause)
               && StringComparer.Ordinal.Equals(cause, EventSubprocessBodySchedulingCause)
               && metadata.TryGetValue(BpmnStartTrigger.StartElementIdMetadataKey, out var hint)
               && !string.IsNullOrWhiteSpace(hint)
            ? hint
            : null;
    }

    /// <summary>
    /// Seeds an event-subprocess body from its scheduled-start hint (spec 128 D2): the single event-start element
    /// named by the hint seeds one token, then the body runs as an ordinary nested process. A hint naming an element
    /// that is not a start event of this process faults deterministically.
    /// </summary>
    private static EvaluationResult SeedFromScheduledStart(BpmnGraph graph, BpmnExecutionState state, string startElementId)
    {
        var startElement = graph.StartEvents.FirstOrDefault(element => StringComparer.Ordinal.Equals(element.ElementId, startElementId));
        if (startElement is null)
        {
            var message = $"BPMN process was scheduled with a start-element hint '{startElementId}', which is not a start event of this process.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, null, null, null, message);
            return new EvaluationResult(state, new ActivityFault("bpmn.start.unresolved-hint", message));
        }

        return SeedToken(state, startElement.ElementId, $"BPMN event-subprocess body start '{startElement.ElementId}' seeded the initial token from a scheduled-start hint.");
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

        var tokenId = ResolveTokenId(context, state, completionContext.CompletedChildExecutableNodeId, completionContext.CompletedChildIterationId);
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

        // spec 121: a multi-instance instance child completed — advance the loop (schedule the next instance
        // in sequential mode) or, on the last instance, tear down the host's listeners and route the host's
        // outbound flows through its normal behavior. Instances never route per-completion, so this short-
        // circuits the normal behavior dispatch below (the instance token is not a race member and its parent
        // coordinator, not the instance, owns any boundary listeners).
        if (token.ParentTokenId is { } instanceParentTokenId
            && FindLoopByCoordinator(state, instanceParentTokenId) is { } loop
            && StringComparer.Ordinal.Equals(token.AtElementId, loop.ElementId))
        {
            // spec 133 D3 (MI composition): the instance interception context is entered first; when the instance is a
            // call activity completing with a failure outcome, the D3 ladder routes here composed with the spec-121
            // coordinator cascade (RouteCallActivityFailureOutcome resolves the interrupt target to the loop
            // coordinator, so firing an error boundary / error event subprocess interrupts every remaining instance).
            if (IsCallActivityFailureCompletion(graph, token, completionContext.OutcomeNames, out var instanceFailureOutcome))
            {
                return ValueTask.FromResult(FinishEvaluation(context, RouteCallActivityFailureOutcome(
                    context, graph, state, token, instanceFailureOutcome, liveChildAeiByNode, pendingCancellations, completionContext.CompletedChildActivityExecutionId)));
            }

            return ValueTask.FromResult(FinishEvaluation(context, HandleMultiInstanceInstanceCompletion(
                context, graph, state, token, loop, liveChildAeiByNode, pendingCancellations, completionContext.CompletedChildActivityExecutionId)));
        }

        // spec 124: a compensation handler sub-token completed — intercept BEFORE behavior dispatch (the MI-instance
        // interception precedent). Consume the sub-token, mark its compensable Compensated, and advance the run:
        // schedule the next claimed handler, or drop the run and complete the coordinating throw token.
        if (token.ParentTokenId is { } handlerParentTokenId
            && FindCompensationRun(state, handlerParentTokenId) is { } compensationRun
            && graph.GetRequiredElement(token.AtElementId).IsForCompensation)
        {
            return ValueTask.FromResult(FinishEvaluation(context, HandleCompensationHandlerCompletion(
                context, graph, state, token, compensationRun, completionContext.CompletedChildActivityExecutionId)));
        }

        // spec 125 D2b: a transaction child that completed with the Cancelled outcome is intercepted BEFORE normal
        // behavior dispatch and BEFORE Case B compensable registration — a cancelled transaction is not
        // successfully-completed work: it registers no compensable, routes no normal outbound, and instead fires
        // its attached cancel boundary (or faults deterministically when none is attached).
        if (graph.GetRequiredElement(token.AtElementId).IsTransaction
            && completionContext.OutcomeNames.Contains(CancelledOutcomeName, StringComparer.Ordinal))
        {
            return ValueTask.FromResult(FinishEvaluation(context, HandleTransactionCancelled(
                context, graph, state, token, liveChildAeiByNode, pendingCancellations, completionContext.CompletedChildActivityExecutionId)));
        }

        // spec 128/134 D5: a completion at a TriggeredByEvent element — intercept BEFORE behavior dispatch (the
        // MI/compensation-handler interception precedent). A listener token and an activation token both sit here, so
        // discriminate on Kind: a Listener completion (spec 134) is a fired scope listener (activate + re-arm/interrupt),
        // BEFORE the tier-1 body-completion check; an Activation (or Kind-less tier-1) completion is the body finishing —
        // the element has no flows, so the activation token is consumed and nothing is routed.
        if (graph.GetRequiredElement(token.AtElementId).TriggeredByEvent)
        {
            if (token.Kind == BpmnTokenKind.Listener && graph.EventSubprocessByElementId(token.AtElementId) is { } firedCatcher)
            {
                return ValueTask.FromResult(FinishEvaluation(context, HandleScopeListenerFired(
                    context, graph, state, token, firedCatcher, liveChildAeiByNode, pendingCancellations, completionContext.CompletedChildActivityExecutionId)));
            }

            return ValueTask.FromResult(FinishEvaluation(context, HandleEventSubprocessBodyCompletion(state, token)));
        }

        // spec 133 D3: a call activity's bound child (a DispatchWorkflow node) completed with a failure outcome
        // (Faulted/DispatchFailed/Cancelled) — intercept BEFORE normal behavior dispatch (joining the MI/compensation/
        // transaction/event-subprocess interception ladder). The completion never routes normal outbound; instead the
        // engine routes the error-catcher ladder directly (host error boundary → scope error event subprocess →
        // composite fault) with NO seam-B absorption (the child is already terminal; no incident exists). The MI-
        // instance case is handled above, inside the instance interception, so the coordinator cascade composes first.
        if (IsCallActivityFailureCompletion(graph, token, completionContext.OutcomeNames, out var callActivityFailureOutcome))
        {
            return ValueTask.FromResult(FinishEvaluation(context, RouteCallActivityFailureOutcome(
                context, graph, state, token, callActivityFailureOutcome, liveChildAeiByNode, pendingCancellations, completionContext.CompletedChildActivityExecutionId)));
        }

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

        // spec 121: a late fault of a child whose token was already cancelled (e.g. a sibling multi-instance
        // instance torn down by an error-boundary absorption or a boundary interrupt) — or after the process
        // terminated — is absorbed by the token-status guard: the subtree reclaim already rode the interrupting
        // evaluation, so the process must not fault a second time (mirrors the completion-path canceled guard).
        var faultedToken = faultedTokenId is not null
            ? state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, faultedTokenId))
            : null;
        if (state.Terminated || faultedToken is { Status: BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled })
        {
            if (faultedChild is not null)
                state = RemoveActiveChild(state, faultedChild.TokenId);
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Canceled, faultedChild?.ElementId, null, faultedToken?.TokenId,
                $"BPMN ignored a late fault of child node '{faultedNodeId}' whose token was already cancelled or the process ended.");
            return ValueTask.FromResult(FinishEvaluation(context, new EvaluationResult(state)));
        }

        // spec 120 D5: absorb the fault through an error boundary attached to the faulted child's host.
        if (faultedChild is not null
            && faultContext.IncidentId is { } incidentId
            && graph.AttachedErrorBoundary(faultedChild.ElementId) is { } errorBoundary
            && state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, faultedChild.TokenId)) is { } hostToken
            && hostToken.Status is not (BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled))
        {
            return ValueTask.FromResult(AbsorbChildFaultThroughErrorBoundary(context, graph, state, faultedChild, hostToken, errorBoundary, incidentId, faultContext.FaultedChildActivityExecutionId));
        }

        // spec 128 D4: no host error boundary, but the scope has an error event subprocess — absorb the fault via seam B
        // (like the boundary path) and activate the event subprocess interrupting. An event-subprocess body's own fault
        // is NOT self-caught (its host element is the TriggeredByEvent element): it rides the ordinary composite-fault path.
        if (faultedChild is not null
            && faultContext.IncidentId is { } errorEventSubprocessIncidentId
            && !graph.GetRequiredElement(faultedChild.ElementId).TriggeredByEvent
            && graph.ErrorEventSubprocess() is { } errorEventSubprocess
            && state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, faultedChild.TokenId)) is { } errorHostToken
            && errorHostToken.Status is not (BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled))
        {
            return ValueTask.FromResult(AbsorbChildFaultThroughErrorEventSubprocess(context, graph, state, faultedChild, errorHostToken, errorEventSubprocess, errorEventSubprocessIncidentId, faultContext.FaultedChildActivityExecutionId));
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
    /// staged for it), cancels the interrupt target (seam-A carry) and its sibling catch listeners, mints an
    /// <c>Active</c> token at the error boundary to route its outbound flows, and finishes through the shared
    /// quiescence machinery. When the faulted child belongs to a multi-instance instance (spec 121), the
    /// interrupt target is the loop coordinator, so cancelling it cascade-cancels every remaining live instance
    /// (the faulted instance's own subtree rides seam B). The absorption request and the seam-A cancellations
    /// are staged only at the clean (Defer/Complete) exit — never on a routing fault.
    /// </summary>
    private RuntimeStructuralContinuation AbsorbChildFaultThroughErrorBoundary(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnActiveChild faultedChild,
        BpmnToken faultedToken,
        BpmnElement errorBoundary,
        string incidentId,
        string faultedChildActivityExecutionId)
    {
        var pendingCancellations = new List<BpmnPendingSubtreeCancellation>();
        var liveChildAeiByNode = BuildLiveChildAeiByNode(context);

        // The interrupt target is the multi-instance loop coordinator when the faulted child is an instance,
        // otherwise the faulted child's own host token.
        var interruptTokenId = ResolveMultiInstanceCoordinatorTokenId(state, faultedToken) ?? faultedToken.TokenId;

        // The faulted child's own subtree is reclaimed by the seam-B absorption plan; only drop its record.
        state = RemoveActiveChild(state, faultedChild.TokenId);

        // Cancel the interrupt target (coordinator → cascades all live instances; host → its bound child was
        // already dropped, so this is a pure token flip) and its still-armed sibling catch listeners.
        state = CancelTokenAndChild(state, interruptTokenId, BoundaryHostInterruptedReason, liveChildAeiByNode, pendingCancellations,
            (token, reason) => $"BPMN error boundary '{errorBoundary.ElementId}' absorbed a child fault of host '{faultedToken.AtElementId}' and cancelled token '{token.TokenId}' at '{token.AtElementId}' ({reason}).");
        state = CancelHostListeners(graph, state, interruptTokenId, listenerTokenToSkip: null, BoundaryHostInterruptedReason, liveChildAeiByNode, pendingCancellations);

        // Mint an active token at the error boundary so its behavior routes the error path; it inherits the
        // interrupt target (host/coordinator) token's loop-iteration key (spec 122).
        var interruptIterationKey = state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, interruptTokenId))?.IterationKey;
        var errorToken = NewToken(state, errorBoundary.ElementId, flowId: null, parentTokenId: interruptTokenId, BpmnTokenStatus.Active, producingActivityExecutionId: faultedChildActivityExecutionId, iterationKey: interruptIterationKey);
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

    /// <summary>
    /// Absorbs the host's child fault through the scope's error event subprocess (spec 128 D4): drops the faulted
    /// child's active-child record (its subtree rides the seam-B absorption plan), then activates the (always
    /// interrupting) error event subprocess — minting a scope-level activation token and stopping all other live
    /// work in the scope (which cancels the faulted host token, its sibling listeners, and any other branch, with
    /// their seam-A subtree cancellations carried) before scheduling the body. The absorption request and the
    /// seam-A cancellations are staged only at the clean (Defer/Complete) exit — never on a routing fault.
    /// </summary>
    private RuntimeStructuralContinuation AbsorbChildFaultThroughErrorEventSubprocess(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnActiveChild faultedChild,
        BpmnToken faultedToken,
        BpmnEventSubprocessCatcher errorEventSubprocess,
        string incidentId,
        string faultedChildActivityExecutionId)
    {
        var pendingCancellations = new List<BpmnPendingSubtreeCancellation>();
        var liveChildAeiByNode = BuildLiveChildAeiByNode(context);

        // The faulted child's own subtree is reclaimed by the seam-B absorption plan; only drop its record so the
        // subsequent stop-others (which cancels the faulted host token) does not stage a duplicate seam-A cancel.
        var faultIterationKey = faultedToken.IterationKey;
        state = RemoveActiveChild(state, faultedChild.TokenId);

        var activation = ActivateEventSubprocess(context, graph, state, errorEventSubprocess, faultIterationKey,
            liveChildAeiByNode, pendingCancellations, faultedChildActivityExecutionId,
            $"child fault of host '{faultedToken.AtElementId}'");
        state = activation.State;

        var result = Propagate(context, graph, state, faultedChildActivityExecutionId) with
        {
            PendingSubtreeCancellations = pendingCancellations,
            FaultAbsorption = new BpmnFaultAbsorption(incidentId, EventSubprocessErrorAbsorptionReason)
        };

        return FinishEvaluation(context, result);
    }

    /// <summary>
    /// True when the completing token is a call activity whose bound child completed with a translated failure
    /// outcome (spec 133 D3): <c>Faulted</c>/<c>DispatchFailed</c>/<c>Cancelled</c>. Yields the matched outcome
    /// (deterministic precedence). <c>Completed</c>/<c>Dispatched</c> return <c>false</c> (normal task-flow routing).
    /// </summary>
    private static bool IsCallActivityFailureCompletion(
        BpmnGraph graph,
        BpmnToken token,
        IReadOnlyCollection<string> outcomeNames,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? failureOutcome)
    {
        failureOutcome = null;
        if (!BpmnElementFamilies.IsCallActivity(graph.GetRequiredElement(token.AtElementId)))
            return false;

        failureOutcome = CallActivityFailureOutcomes.FirstOrDefault(outcome => outcomeNames.Contains(outcome, StringComparer.Ordinal));
        return failureOutcome is not null;
    }

    /// <summary>
    /// Routes a call activity's translated failure outcome (spec 133 D3), the completion-path analog of the spec
    /// 120/128 seam-B fault absorptions but WITHOUT any seam B: a faulted/cancelled/dispatch-failed called workflow
    /// COMPLETES the DispatchWorkflow child with an outcome (it never faults, produces no incident), so there is
    /// nothing to absorb — the host token is consumed and the error-catcher ladder is routed directly. The completing
    /// child's active-child record was already dropped by the caller. The ladder: (1) a host-attached error boundary
    /// mints an <c>Active</c> token at the boundary (inheriting the interrupt target's iteration key) and propagates;
    /// (2) else the scope's error event subprocess activates interrupting (spec-128 activation); (3) else the process
    /// faults deterministically with the per-outcome composite fault code. The interrupt target is the multi-instance
    /// loop coordinator when the completing token is an instance (spec 121 cascade — firing a catcher interrupts every
    /// remaining instance), otherwise the completing host token itself. NO seam-B request is staged on this path.
    /// </summary>
    private EvaluationResult RouteCallActivityFailureOutcome(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken completingToken,
        string failureOutcome,
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations,
        string completedActivityExecutionId)
    {
        var element = graph.GetRequiredElement(completingToken.AtElementId);

        // The interrupt target is the MI loop coordinator when the completing token is an instance (spec 121
        // coordinator cascade), otherwise the completing host token itself.
        var interruptTokenId = ResolveMultiInstanceCoordinatorTokenId(state, completingToken) ?? completingToken.TokenId;
        var interruptIterationKey = state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, interruptTokenId))?.IterationKey;

        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.CallActivityFailureRouted, element.ElementId, null, completingToken.TokenId,
            $"BPMN call activity '{element.ElementId}' completed with the '{failureOutcome}' outcome; routing the call-activity failure path.");

        // Ladder tier 1: a host-attached error boundary (spec 120) fires — mint a token to route its outbound flows.
        if (graph.AttachedErrorBoundary(element.ElementId) is { } errorBoundary)
        {
            state = CancelTokenAndChild(state, interruptTokenId, CallActivityFailureRoutedReason, liveChildAeiByNode, pendingCancellations,
                (token, reason) => $"BPMN error boundary '{errorBoundary.ElementId}' routed a call-activity '{failureOutcome}' outcome of host '{element.ElementId}' and cancelled token '{token.TokenId}' at '{token.AtElementId}' ({reason}).");
            state = CancelHostListeners(graph, state, interruptTokenId, listenerTokenToSkip: null, CallActivityFailureRoutedReason, liveChildAeiByNode, pendingCancellations);

            var errorToken = NewToken(state, errorBoundary.ElementId, flowId: null, parentTokenId: interruptTokenId, BpmnTokenStatus.Active, producingActivityExecutionId: completedActivityExecutionId, iterationKey: interruptIterationKey);
            state = AddToken(state, errorToken);
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.TokenEmitted, errorBoundary.ElementId, null, errorToken.TokenId,
                $"BPMN error boundary '{errorBoundary.ElementId}' fired and emitted token '{errorToken.TokenId}' to route the call-activity failure path.");

            var boundaryResult = Propagate(context, graph, state, completedActivityExecutionId);
            return boundaryResult with { PendingSubtreeCancellations = pendingCancellations };
        }

        // Ladder tier 2: the scope's error event subprocess (spec 128) activates interrupting — stop all other live
        // work (which cancels the interrupt target and its listeners) and schedule the body.
        if (graph.ErrorEventSubprocess() is { } errorEventSubprocess)
        {
            var activation = ActivateEventSubprocess(context, graph, state, errorEventSubprocess, interruptIterationKey,
                liveChildAeiByNode, pendingCancellations, completedActivityExecutionId,
                $"call-activity '{failureOutcome}' outcome of host '{element.ElementId}'");
            var subprocessResult = Propagate(context, graph, activation.State, completedActivityExecutionId);
            return subprocessResult with { PendingSubtreeCancellations = pendingCancellations };
        }

        // Ladder tier 3: no catcher — the process faults deterministically with the per-outcome composite fault code.
        // Consume the interrupt target (coordinator cascade or host token) and its listeners; a fault continuation
        // drops the carried seam-A cancellations (the whole process is faulting).
        state = CancelTokenAndChild(state, interruptTokenId, CallActivityFailureRoutedReason, liveChildAeiByNode, pendingCancellations,
            (token, reason) => $"BPMN call activity '{element.ElementId}' faulted and cancelled token '{token.TokenId}' at '{token.AtElementId}' ({reason}).");
        state = CancelHostListeners(graph, state, interruptTokenId, listenerTokenToSkip: null, CallActivityFailureRoutedReason, liveChildAeiByNode, pendingCancellations);

        var faultCode = failureOutcome switch
        {
            CallActivityFaultedOutcomeName => CallActivityFaultedFaultCode,
            CallActivityDispatchFailedOutcomeName => CallActivityDispatchFailedFaultCode,
            _ => CallActivityCancelledFaultCode
        };
        var message = $"BPMN call activity '{element.ElementId}' completed with the '{failureOutcome}' outcome, but no error boundary or error event subprocess is available to route it.";
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, element.ElementId, null, completingToken.TokenId, message);
        return new EvaluationResult(state, new ActivityFault(faultCode, message));
    }

    /// <summary>
    /// Raises an escalation from an escalation throw/end event (spec 127 D2a / spec 128 D3), on the thrower's own
    /// (nested) evaluation. First consults the throwing scope's OWN escalation event subprocesses (spec 128; exact
    /// code beats catch-all): a match returns that catcher so the caller activates it (D5) instead of staging
    /// upward — preserving one-hop bubbling (each scope checks itself before passing the signal up). On no own-scope
    /// match, when this process has a committed parent it stages a seam-C <c>RequestParentNotification</c>
    /// (code <see cref="EscalationNotificationCode"/>, payload <c>{ code, name? }</c>) that rides the evaluation's
    /// Defer/Complete commit; at a root process it is a no-op with an <c>EscalationUnhandled</c> diagnostic. Never
    /// faults. The companion routing command routes/consumes as usual.
    /// </summary>
    private static (BpmnExecutionState State, BpmnEventSubprocessCatcher? OwnScopeActivation) RaiseEscalation(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken token,
        BpmnElement element)
    {
        var (code, name) = ReadEscalation(element);

        // spec 128 D3: own-scope catch — an escalation event subprocess in the throwing scope catches it before bubbling.
        if (graph.EscalationEventSubprocess(code) is { } catcher)
        {
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EscalationCaught, element.ElementId, null, token.TokenId,
                $"BPMN escalation event '{element.ElementId}' raised escalation code '{code}', caught by own-scope event subprocess '{catcher.ElementId}'.");
            return (state, catcher);
        }

        if (context.ActivityExecutionState.ParentActivityExecutionId is not null)
        {
            context.RequestParentNotification(EscalationNotificationCode, BuildEscalationPayload(code, name));
            return (BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EscalationRaised, element.ElementId, null, token.TokenId,
                $"BPMN escalation event '{element.ElementId}' raised escalation code '{code}' to its parent scope."), null);
        }

        return (BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EscalationUnhandled, element.ElementId, null, token.TokenId,
            $"BPMN escalation event '{element.ElementId}' raised escalation code '{code}' at a root process; no parent scope can catch it (no-op)."), null);
    }

    /// <summary>
    /// Activates an event subprocess (spec 128 D5): mints a scope-level activation token at the event-subprocess
    /// element (<c>AwaitingChild</c>, <c>ParentTokenId</c> null, inheriting <paramref name="triggerIterationKey"/>
    /// where one exists), and schedules the bound body child seeded at its single event-start element via the D2
    /// hint. An interrupting activation first stops all OTHER live work in the scope through the shared
    /// <see cref="StopOtherLiveWork"/> helper (the coordinator cascades and seam-A carries honored per the caller's
    /// live-child map), keeping the activation token alive so the scope survives until the body completes. Non-
    /// interrupting activations leave other scope work untouched (repeated/concurrent activations are independent —
    /// distinct tokens and a fresh body schedule). Does not propagate; the caller drives propagation and staging.
    /// </summary>
    private EvaluationResult ActivateEventSubprocess(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnEventSubprocessCatcher catcher,
        string? triggerIterationKey,
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations,
        string producingActivityExecutionId,
        string triggerDescription)
    {
        var ownerActivityExecutionId = context.ActivityExecutionState.Execution.ActivityExecutionId;

        var activationToken = NewToken(state, catcher.ElementId, flowId: null, parentTokenId: null, BpmnTokenStatus.AwaitingChild,
            producingActivityExecutionId: producingActivityExecutionId, iterationKey: triggerIterationKey, kind: BpmnTokenKind.Activation);
        state = AddToken(state, activationToken);
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EventSubprocessActivated, catcher.ElementId, null, activationToken.TokenId,
            $"BPMN {(catcher.Interrupting ? "interrupting" : "non-interrupting")} event subprocess '{catcher.ElementId}' activated ({triggerDescription}).");

        // Interrupting: stop all OTHER live work in the scope, keeping the activation token alive as the scope's work.
        if (catcher.Interrupting)
            state = StopOtherLiveWork(state, activationToken.TokenId, EventSubprocessScopeInterruptedReason, liveChildAeiByNode, pendingCancellations,
                (stoppedToken, reason) => $"BPMN event subprocess '{catcher.ElementId}' interrupted the scope and cancelled token '{stoppedToken.TokenId}' at '{stoppedToken.AtElementId}' ({reason}).").State;

        state = BpmnScheduler.ScheduleChild(context, state, catcher.ChildNodeId, catcher.ElementId, activationToken.TokenId, ownerActivityExecutionId,
            EventSubprocessBodySchedulingCause, startElementId: catcher.BodyStartElementId);
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Scheduled, catcher.ElementId, null, activationToken.TokenId,
            $"BPMN event subprocess '{catcher.ElementId}' scheduled body '{catcher.ChildNodeId}' seeded at start element '{catcher.BodyStartElementId}'.");

        return new EvaluationResult(state, PendingSubtreeCancellations: pendingCancellations);
    }

    /// <summary>
    /// Handles an event-subprocess body completion (spec 128 D5), intercepted before behavior dispatch. The
    /// event-subprocess element has no outbound flows, so the activation token is simply consumed and nothing is
    /// routed; the scope's ordinary liveness/completion accounting then proceeds.
    /// </summary>
    private static EvaluationResult HandleEventSubprocessBodyCompletion(BpmnExecutionState state, BpmnToken activationToken)
    {
        state = UpdateToken(state, activationToken with { Status = BpmnTokenStatus.Consumed });
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EventSubprocessCompleted, activationToken.AtElementId, null, activationToken.TokenId,
            $"BPMN event subprocess '{activationToken.AtElementId}' body completed; the activation token is consumed and nothing is routed.");
        return new EvaluationResult(state);
    }

    /// <summary>
    /// Handles a fired tier-2 scope listener (spec 134 D2), intercepted before behavior dispatch. The listener child
    /// (a suspending <c>Event</c>/<c>Delay</c>) resumed on its stimulus, so the event subprocess activates. The firing
    /// listener token is consumed (its child was already dropped at the top of <see cref="OnChildCompletedAsync"/>).
    /// A <b>non-interrupting</b> catcher re-arms a fresh listener FIRST (deterministic ids from <c>Sequence</c>; timer
    /// repetition falls out of the re-arm loop, each arm a single one-shot child) and then activates the body
    /// non-interrupting alongside untouched scope work. An <b>interrupting</b> catcher does not re-arm: the activation's
    /// <see cref="StopOtherLiveWork"/> drains every other live token — including sibling scope listeners (ordinary live
    /// tokens there, their suspended children riding the seam-A carries) — before scheduling the body.
    /// </summary>
    private EvaluationResult HandleScopeListenerFired(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken listenerToken,
        BpmnEventSubprocessCatcher catcher,
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations,
        string completedActivityExecutionId)
    {
        var ownerActivityExecutionId = context.ActivityExecutionState.Execution.ActivityExecutionId;

        state = UpdateToken(state, listenerToken with { Status = BpmnTokenStatus.Consumed });
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.ScopeListenerFired, catcher.ElementId, null, listenerToken.TokenId,
            $"BPMN {(catcher.Interrupting ? "interrupting" : "non-interrupting")} {catcher.TriggerKind} scope listener for event subprocess '{catcher.ElementId}' fired.");

        if (!catcher.Interrupting)
            state = ArmScopeListener(context, state, catcher, ownerActivityExecutionId);

        var activation = ActivateEventSubprocess(context, graph, state, catcher, listenerToken.IterationKey,
            liveChildAeiByNode, pendingCancellations, completedActivityExecutionId, $"scope listener '{catcher.ElementId}' fired");
        state = activation.State;

        var result = Propagate(context, graph, state, ownerActivityExecutionId);
        return result with { PendingSubtreeCancellations = pendingCancellations };
    }

    /// <summary>
    /// Phase 1 of scope-listener arming (spec 134 D2): mints one <see cref="BpmnTokenKind.Listener"/> token per
    /// external-trigger (message/signal/timer) event subprocess at its element (<c>AwaitingChild</c>,
    /// <c>ParentTokenId</c> null), in deterministic element-id ordinal order, BEFORE the seed propagates — so an
    /// interrupting activation raised during that propagation drains the listener tokens as ordinary live tokens. Their
    /// suspending children are scheduled later (<see cref="ScheduleScopeListenerChildren"/>).
    /// </summary>
    private static BpmnExecutionState MintScopeListenerTokens(BpmnGraph graph, BpmnExecutionState state, string schedulingActivityExecutionId)
    {
        foreach (var catcher in graph.ExternalTriggerEventSubprocesses)
        {
            var listenerToken = NewToken(state, catcher.ElementId, flowId: null, parentTokenId: null, BpmnTokenStatus.AwaitingChild,
                producingActivityExecutionId: schedulingActivityExecutionId, iterationKey: null, kind: BpmnTokenKind.Listener);
            state = AddToken(state, listenerToken);
        }

        return state;
    }

    /// <summary>
    /// Phase 2 of scope-listener arming (spec 134 D2): schedules the suspending listener child (the synthesized
    /// <c>Delay</c>/<c>Event</c>) for each still-live listener token that has no child yet, in deterministic element-id
    /// ordinal order. Called AFTER the seed propagates and only when real work remains (an active child is pending), so
    /// the runtime never schedules a listener child in a terminal evaluation. A listener token drained by an interrupting
    /// activation during propagation is no longer live and is skipped.
    /// </summary>
    private static BpmnExecutionState ScheduleScopeListenerChildren(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        string schedulingActivityExecutionId)
    {
        var pendingListenerTokens = state.Tokens
            .Where(token => token.Kind == BpmnTokenKind.Listener && token.Status == BpmnTokenStatus.AwaitingChild
                            && !state.ActiveChildren.Any(child => StringComparer.Ordinal.Equals(child.TokenId, token.TokenId)))
            .OrderBy(token => token.AtElementId, StringComparer.Ordinal)
            .ToArray();

        foreach (var listenerToken in pendingListenerTokens)
        {
            var catcher = graph.EventSubprocessByElementId(listenerToken.AtElementId)
                ?? throw new BpmnExecutionException($"BPMN scope listener token '{listenerToken.TokenId}' resolved no event subprocess at '{listenerToken.AtElementId}'.");
            state = ScheduleScopeListenerChild(context, state, catcher, listenerToken.TokenId, schedulingActivityExecutionId);
        }

        return state;
    }

    /// <summary>
    /// Arms a fresh tier-2 scope listener in one shot (spec 134 D2 re-arm): mints a <see cref="BpmnTokenKind.Listener"/>
    /// token and schedules its listener child together. Used to re-arm after a non-interrupting fire (which always then
    /// schedules the body, so the evaluation defers — never a terminal evaluation).
    /// </summary>
    private static BpmnExecutionState ArmScopeListener(
        IRuntimeActivityExecutionContext context,
        BpmnExecutionState state,
        BpmnEventSubprocessCatcher catcher,
        string schedulingActivityExecutionId)
    {
        var listenerToken = NewToken(state, catcher.ElementId, flowId: null, parentTokenId: null, BpmnTokenStatus.AwaitingChild,
            producingActivityExecutionId: schedulingActivityExecutionId, iterationKey: null, kind: BpmnTokenKind.Listener);
        state = AddToken(state, listenerToken);
        return ScheduleScopeListenerChild(context, state, catcher, listenerToken.TokenId, schedulingActivityExecutionId);
    }

    /// <summary>Schedules a listener token's suspending child (the synthesized <c>Delay</c>/<c>Event</c>) and records the <c>ScopeListenerArmed</c> diagnostic (spec 134 D2).</summary>
    private static BpmnExecutionState ScheduleScopeListenerChild(
        IRuntimeActivityExecutionContext context,
        BpmnExecutionState state,
        BpmnEventSubprocessCatcher catcher,
        string listenerTokenId,
        string schedulingActivityExecutionId)
    {
        var listenerNodeId = catcher.ListenerNodeId
            ?? throw new BpmnExecutionException($"BPMN event subprocess '{catcher.ElementId}' has no scope-listener node to arm; validation should have required one for a message/signal/timer trigger.");

        state = BpmnScheduler.ScheduleChild(context, state, listenerNodeId, catcher.ElementId, listenerTokenId, schedulingActivityExecutionId, ScopeListenerSchedulingCause);
        return BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.ScopeListenerArmed, catcher.ElementId, null, listenerTokenId,
            $"BPMN event subprocess '{catcher.ElementId}' armed a {catcher.TriggerKind} scope listener (child '{listenerNodeId}').");
    }

    /// <summary>
    /// Handles a non-terminal child→parent escalation notification (spec 127 D2b, seam C), on the parent scope's
    /// evaluation. Resolves the notifying child to its host element, matches the payload's escalation code against
    /// the host's attached escalation boundaries (exact code beats the code-less catch-all), and fires: a
    /// non-interrupting match mints a boundary token alongside the untouched host (repeated notifications fire
    /// repeatedly); an interrupting match cancels the host token (its subtree torn down via seam A, MI coordinator
    /// cascades honored) before routing the boundary path. An unmatched escalation bubbles (re-staged to the
    /// grandparent when this process itself has a parent, else a root <c>EscalationUnhandled</c> no-op). Late races
    /// (notifying child no longer live / host token terminal) fire non-interrupting boundaries but no-op
    /// interrupting ones with an <c>EscalationLate</c> diagnostic. Any non-escalation seam-C code is a
    /// forward-compatible diagnostic pass-through. Never faults.
    /// </summary>
    public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notification);

        var graph = context.ExecutableNode.GetOrAddRoutingStructure(BpmnGraph.From);
        var state = BpmnStatePersister.LoadState(context.ActivityExecutionState) ?? BpmnStatePersister.CreateInitialState();

        // spec 127 D2b: a non-escalation seam-C code (a future consumer's) is ignored with a diagnostic — never a fault.
        if (!StringComparer.Ordinal.Equals(notification.Code, EscalationNotificationCode))
        {
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EscalationUnhandled, null, null, null,
                $"BPMN process ignored a child notification with unrecognized seam-C code '{notification.Code}' (forward-compatible pass-through).");
            return ValueTask.FromResult(FinishEvaluation(context, new EvaluationResult(state)));
        }

        return ValueTask.FromResult(FinishEvaluation(context, HandleEscalationNotification(context, graph, state, notification)));
    }

    /// <summary>Resolves, matches, and fires an escalation notification (spec 127 D2b); the escalation-specific body of <see cref="OnChildNotifiedAsync"/>.</summary>
    private EvaluationResult HandleEscalationNotification(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        ActivityChildNotifiedContext notification)
    {
        var escalationCode = ReadPayloadCode(notification.Payload);

        // Resolve the host element from the notifying child's node id (robust to the late case: the host element
        // is graph-derived, so it resolves even after the notifying child stopped being an active child).
        var hostElement = graph.FindElementByChildNodeId(notification.NotifyingChildExecutableNodeId);
        if (hostElement is null || escalationCode is null)
            return BubbleOrUnhandled(context, state, notification, escalationCode, hostElementId: hostElement?.ElementId);

        // The notifying child's active-child record (present in the live case; absent in the late case — the child
        // already completed/faulted and its record was dropped).
        var activeChild = state.ActiveChildren.FirstOrDefault(child =>
            StringComparer.Ordinal.Equals(child.NodeId, notification.NotifyingChildExecutableNodeId) &&
            StringComparer.Ordinal.Equals(child.IterationId, notification.NotifyingChildIterationId));
        var hostToken = activeChild is not null
            ? state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, activeChild.TokenId))
            : null;
        var isLate = hostToken is null || hostToken.Status is BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled;

        // spec 128 D3: the specificity ladder — (1) host boundary exact, (2) scope event subprocess exact, (3) host
        // boundary catch-all, (4) scope event subprocess catch-all, (5) bubble. Exact beats catch-all across kinds;
        // within a level the host boundary (more local to the throwing child) beats the scope-level subprocess.
        var boundaries = graph.AttachedEscalationBoundaries(hostElement.ElementId);
        var boundaryExact = boundaries.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(ReadEscalationBoundaryCode(candidate), escalationCode));
        var boundaryCatchAll = boundaries.FirstOrDefault(candidate => ReadEscalationBoundaryCode(candidate) is null);
        var eventSubprocessExact = graph.EscalationEventSubprocessExact(escalationCode);
        var eventSubprocessCatchAll = graph.EscalationCatchAllEventSubprocess();

        if (boundaryExact is not null)
            return FireEscalationBoundaryMatch(context, graph, state, boundaryExact, hostElement, hostToken, isLate, notification, escalationCode);
        if (eventSubprocessExact is not null)
            return FireEscalationEventSubprocessMatch(context, graph, state, eventSubprocessExact, hostToken, notification, escalationCode);
        if (boundaryCatchAll is not null)
            return FireEscalationBoundaryMatch(context, graph, state, boundaryCatchAll, hostElement, hostToken, isLate, notification, escalationCode);
        if (eventSubprocessCatchAll is not null)
            return FireEscalationEventSubprocessMatch(context, graph, state, eventSubprocessCatchAll, hostToken, notification, escalationCode);

        return BubbleOrUnhandled(context, state, notification, escalationCode, hostElement.ElementId);
    }

    /// <summary>
    /// Fires a matched escalation boundary (spec 127 D2b; byte-identical to spec 127): a non-interrupting boundary
    /// fires (live or late) alongside the untouched host; an interrupting boundary cancels the host token (or its MI
    /// coordinator) and routes, unless the host had already terminalized (a late race no-ops with an
    /// <c>EscalationLate</c> diagnostic — interrupting it retroactively would double-route its outbound).
    /// </summary>
    private EvaluationResult FireEscalationBoundaryMatch(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnElement boundary,
        BpmnElement hostElement,
        BpmnToken? hostToken,
        bool isLate,
        ActivityChildNotifiedContext notification,
        string escalationCode)
    {
        if (!boundary.CancelActivity)
            return FireEscalationBoundary(context, graph, state, boundary, hostToken, interruptTokenId: null, notification, escalationCode);

        if (isLate)
        {
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EscalationLate, boundary.ElementId, null, hostToken?.TokenId,
                $"BPMN interrupting escalation boundary '{boundary.ElementId}' matched code '{escalationCode}', but host '{hostElement.ElementId}' had already terminalized; no-op (late race).");
            return new EvaluationResult(state);
        }

        // The interrupt target is the multi-instance loop coordinator when the notifying child is an instance,
        // otherwise the host token itself (the spec 120 error-boundary interrupt precedent).
        var interruptTokenId = ResolveMultiInstanceCoordinatorTokenId(state, hostToken!) ?? hostToken!.TokenId;
        return FireEscalationBoundary(context, graph, state, boundary, hostToken, interruptTokenId, notification, escalationCode);
    }

    /// <summary>
    /// Fires a matched scope escalation event subprocess on the notification path (spec 128 D3/D5): activates the
    /// catcher (interrupting stops all other live work in the scope — including the host subprocess whose child
    /// escalated — with seam-A carries staged at the clean exit; non-interrupting leaves other scope work untouched),
    /// inheriting the escalation host token's iteration key, then propagates the body schedule.
    /// </summary>
    private EvaluationResult FireEscalationEventSubprocessMatch(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnEventSubprocessCatcher catcher,
        BpmnToken? hostToken,
        ActivityChildNotifiedContext notification,
        string escalationCode)
    {
        var pendingCancellations = new List<BpmnPendingSubtreeCancellation>();
        var liveChildAeiByNode = catcher.Interrupting ? BuildLiveChildAeiByNode(context) : NoLiveChildren;
        var activation = ActivateEventSubprocess(context, graph, state, catcher, hostToken?.IterationKey, liveChildAeiByNode,
            pendingCancellations, notification.NotifyingChildActivityExecutionId, $"escalation code '{escalationCode}'");

        var result = Propagate(context, graph, activation.State, context.ActivityExecutionState.Execution.ActivityExecutionId);
        return result with { PendingSubtreeCancellations = pendingCancellations };
    }

    /// <summary>
    /// Fires a matched escalation boundary (spec 127 D2b). When <paramref name="interruptTokenId"/> is set
    /// (interrupting), cancels that token via the existing cascade (its subtree torn down via seam A; an MI
    /// coordinator cascade-cancels every instance) and its still-armed sibling catch listeners before minting the
    /// boundary token; otherwise (non-interrupting) leaves the host and its child untouched. The minted
    /// <c>Active</c> token inherits the host token's loop-iteration key and propagates the boundary's outbound
    /// flows. Seam-A cancellations ride the clean continuation.
    /// </summary>
    private EvaluationResult FireEscalationBoundary(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnElement boundary,
        BpmnToken? hostToken,
        string? interruptTokenId,
        ActivityChildNotifiedContext notification,
        string escalationCode)
    {
        var pendingCancellations = new List<BpmnPendingSubtreeCancellation>();

        if (interruptTokenId is not null)
        {
            var liveChildAeiByNode = BuildLiveChildAeiByNode(context);
            state = CancelTokenAndChild(state, interruptTokenId, EscalationHostInterruptedReason, liveChildAeiByNode, pendingCancellations,
                (token, reason) => $"BPMN interrupting escalation boundary '{boundary.ElementId}' matched code '{escalationCode}' and cancelled token '{token.TokenId}' at '{token.AtElementId}' ({reason}).");
            state = CancelHostListeners(graph, state, interruptTokenId, listenerTokenToSkip: null, EscalationHostInterruptedReason, liveChildAeiByNode, pendingCancellations);
        }

        var iterationKey = hostToken?.IterationKey;
        var boundaryToken = NewToken(state, boundary.ElementId, flowId: null, parentTokenId: hostToken?.TokenId, BpmnTokenStatus.Active,
            producingActivityExecutionId: notification.NotifyingChildActivityExecutionId, iterationKey: iterationKey);
        state = AddToken(state, boundaryToken);
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EscalationCaught, boundary.ElementId, null, boundaryToken.TokenId,
            $"BPMN {(interruptTokenId is null ? "non-interrupting" : "interrupting")} escalation boundary '{boundary.ElementId}' caught escalation code '{escalationCode}' and emitted token '{boundaryToken.TokenId}' to route the escalation path.");

        var schedulingActivityExecutionId = context.ActivityExecutionState.Execution.ActivityExecutionId;
        var result = Propagate(context, graph, state, schedulingActivityExecutionId);
        return result with { PendingSubtreeCancellations = pendingCancellations };
    }

    /// <summary>
    /// Bubbles an unmatched escalation (spec 127 D2b): when this process itself has a committed parent, carries a
    /// seam-C re-stage of the identical code/payload to the grandparent (consumer-side recursion, one hop per
    /// level); at a root process it is a no-op with an <c>EscalationUnhandled</c> diagnostic.
    /// </summary>
    private static EvaluationResult BubbleOrUnhandled(
        IRuntimeActivityExecutionContext context,
        BpmnExecutionState state,
        ActivityChildNotifiedContext notification,
        string? escalationCode,
        string? hostElementId)
    {
        if (context.ActivityExecutionState.ParentActivityExecutionId is not null)
        {
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EscalationRaised, hostElementId, null, null,
                $"BPMN process found no escalation boundary for code '{escalationCode ?? "(none)"}'{(hostElementId is null ? "" : $" on host '{hostElementId}'")}; bubbling the escalation to its parent scope.");
            return new EvaluationResult(state, PendingParentNotification: new BpmnPendingParentNotification(notification.Code, notification.Payload));
        }

        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.EscalationUnhandled, hostElementId, null, null,
            $"BPMN root process found no escalation boundary for code '{escalationCode ?? "(none)"}'{(hostElementId is null ? "" : $" on host '{hostElementId}'")}; the escalation is unhandled (no-op).");
        return new EvaluationResult(state);
    }

    /// <summary>The escalation code+name an escalation throw/end event carries (spec 127); the code is validated non-empty at graph build.</summary>
    private static (string Code, string? Name) ReadEscalation(BpmnElement element)
    {
        var properties = element.EventDefinitions.Single().Properties;
        var code = properties.TryGetValue(BpmnEventDefinitionProperties.Code, out var codeValue) && !string.IsNullOrWhiteSpace(codeValue)
            ? codeValue.Trim()
            : throw new BpmnExecutionException($"BPMN escalation event '{element.ElementId}' declares no escalation code; the graph validator should have rejected it.");
        var name = properties.TryGetValue(BpmnEventDefinitionProperties.Name, out var nameValue) && !string.IsNullOrWhiteSpace(nameValue)
            ? nameValue.Trim()
            : null;
        return (code, name);
    }

    /// <summary>The escalation code an escalation boundary declares (spec 127), or <c>null</c> when it is the code-less catch-all.</summary>
    private static string? ReadEscalationBoundaryCode(BpmnElement boundary) =>
        boundary.EventDefinitions.SingleOrDefault() is { } definition
        && definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Code, out var code)
        && !string.IsNullOrWhiteSpace(code)
            ? code.Trim()
            : null;

    /// <summary>Builds the seam-C escalation payload <c>{ code, name? }</c> (spec 127 D2a); the escalation identity travels here, keeping the seam-C code namespace clean. <c>name</c> is omitted when absent.</summary>
    private static JsonElement BuildEscalationPayload(string code, string? name) =>
        JsonSerializer.SerializeToElement(new EscalationPayload(code, name), EscalationPayloadOptions);

    /// <summary>Reads the escalation code from a seam-C escalation payload (spec 127 D2b); <c>null</c> when absent/malformed (defensive — the module builds the payload, so unreachable in practice).</summary>
    private static string? ReadPayloadCode(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.Object } element
        && element.TryGetProperty("code", out var code)
        && code.ValueKind == JsonValueKind.String
        && code.GetString() is { Length: > 0 } value
            ? value
            : null;

    private static readonly JsonSerializerOptions EscalationPayloadOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The seam-C escalation payload shape (spec 127 D2a): the required matching <c>code</c> and an optional display <c>name</c>.</summary>
    private sealed record EscalationPayload(string Code, string? Name = null);

    /// <summary>
    /// Handles a transaction child that completed with the <c>Cancelled</c> outcome (spec 125 D2b), in the parent
    /// scope. A cancelled transaction is not successfully-completed work: it consumes the host token, registers
    /// <b>no</b> compensable and routes <b>no</b> normal outbound flow, tears down the host's still-armed catch
    /// listeners (the Case B teardown, reused), and mints an <c>Active</c> token at the attached cancel boundary
    /// (the error-boundary minting pattern, inheriting the host token's iteration key) so the boundary routes the
    /// cancellation path. When no cancel boundary is attached the parent faults deterministically
    /// (<c>bpmn.transaction.cancelled-unhandled</c>) — cross-scope validation cannot see the nested cancel end
    /// (isolation), so this is a runtime rule.
    /// </summary>
    private EvaluationResult HandleTransactionCancelled(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken hostToken,
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations,
        string completedActivityExecutionId)
    {
        var hostElement = graph.GetRequiredElement(hostToken.AtElementId);

        // Tear down the host's still-armed catch listeners (reused Case B teardown), then consume the host token —
        // no compensable is registered (the nested scope compensated itself) and no normal outbound is routed.
        state = CancelHostListeners(graph, state, hostToken.TokenId, listenerTokenToSkip: null, BoundarySupersededByHostCompletionReason, liveChildAeiByNode, pendingCancellations);
        state = UpdateToken(state, hostToken with { Status = BpmnTokenStatus.Consumed });
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.TransactionCancelled, hostElement.ElementId, null, hostToken.TokenId,
            $"BPMN transaction '{hostElement.ElementId}' completed with the '{CancelledOutcomeName}' outcome; routing the cancellation path.");

        var cancelBoundary = graph.AttachedCancelBoundary(hostElement.ElementId);
        if (cancelBoundary is null)
        {
            var message = $"BPMN transaction '{hostElement.ElementId}' completed with the '{CancelledOutcomeName}' outcome, but no cancel boundary event is attached to route the cancellation.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, hostElement.ElementId, null, hostToken.TokenId, message);
            return new EvaluationResult(state, new ActivityFault(TransactionCancelledUnhandledFaultCode, message));
        }

        // Mint an Active token at the cancel boundary (error-boundary minting pattern), inheriting the host token's
        // iteration key, and propagate — the boundary's behavior routes its outbound flows unconditionally.
        var boundaryToken = NewToken(state, cancelBoundary.ElementId, flowId: null, parentTokenId: hostToken.TokenId, BpmnTokenStatus.Active, producingActivityExecutionId: completedActivityExecutionId, iterationKey: hostToken.IterationKey);
        state = AddToken(state, boundaryToken);
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.TokenEmitted, cancelBoundary.ElementId, null, boundaryToken.TokenId,
            $"BPMN cancel boundary '{cancelBoundary.ElementId}' fired and emitted token '{boundaryToken.TokenId}' to route the cancellation path.");

        var result = Propagate(context, graph, state, completedActivityExecutionId);
        return result with { PendingSubtreeCancellations = pendingCancellations };
    }

    /// <summary>The multi-instance loop coordinator token id when <paramref name="token"/> is one of that loop's instance sub-tokens (spec 121); <c>null</c> for an ordinary (non-instance) token.</summary>
    private static string? ResolveMultiInstanceCoordinatorTokenId(BpmnExecutionState state, BpmnToken token) =>
        token.ParentTokenId is { } parent
        && FindLoopByCoordinator(state, parent) is { } loop
        && StringComparer.Ordinal.Equals(token.AtElementId, loop.ElementId)
            ? parent
            : null;

    /// <summary>
    /// Starts a multi-instance loop (spec 121 D2a): the arriving token becomes the loop coordinator (stays
    /// <c>AwaitingChild</c>), a <see cref="BpmnLoopState"/> record is written, the host's catch boundaries arm
    /// once (parented to the coordinator, D2e), and the bound child is scheduled on private per-instance
    /// sub-tokens — one at a time in sequential mode, all up front in parallel mode. An empty loop
    /// (<c>total == 0</c>, unreachable via a validated cardinality ≥ 1) routes the host's outbound flows
    /// immediately.
    /// </summary>
    private EvaluationResult StartMultiInstanceLoop(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnElement element,
        BpmnToken coordinatorToken,
        string childNodeId,
        string schedulingActivityExecutionId)
    {
        var loopCharacteristics = element.LoopCharacteristics!;

        // Resolve the instance count N and, in collection mode, the per-instance item snapshot. Cardinality mode
        // takes N from the literal; collection mode reads the collection variable ONCE (a documented snapshot,
        // spec 123 D2) through the runtime scoped-variable read seam. A collection read failure faults
        // deterministically.
        IReadOnlyList<JsonElement>? items = null;
        int total;
        if (loopCharacteristics.IsCollectionMode)
        {
            var resolution = ResolveCollectionInstances(context, state, element, loopCharacteristics);
            if (resolution.Fault is not null)
                return new EvaluationResult(resolution.State, resolution.Fault);
            state = resolution.State;
            items = resolution.Items;
            total = items?.Count ?? 0;
        }
        else
        {
            // A null cardinality here would be a graph the validator should already have rejected.
            total = loopCharacteristics.Cardinality
                ?? throw new BpmnExecutionException($"BPMN multi-instance element '{element.ElementId}' resolved no instance count; its loop characteristics declare neither a cardinality nor a collection.");
        }

        if (total <= 0)
            return RouteMultiInstanceCoordinatorOutbound(context, graph, state, element, coordinatorToken, schedulingActivityExecutionId);

        state = UpdateToken(state, coordinatorToken with { Status = BpmnTokenStatus.AwaitingChild });
        var (stateWithLoop, loop) = AddLoop(state, coordinatorToken.TokenId, element.ElementId, loopCharacteristics.IsSequential, total, items);
        state = stateWithLoop;
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Scheduled, element.ElementId, null, coordinatorToken.TokenId,
            $"BPMN multi-instance element '{element.ElementId}' started a {(loopCharacteristics.IsSequential ? "sequential" : "parallel")} loop of {total} instance(s).");

        // spec 120/121 D2e: arm the host's catch boundaries once, parented to the coordinator token.
        state = ArmCatchBoundaries(context, graph, state, element, coordinatorToken.TokenId, schedulingActivityExecutionId);

        var instanceCount = loopCharacteristics.IsSequential ? 1 : total;
        for (var index = 0; index < instanceCount; index++)
            state = ScheduleMultiInstanceInstance(context, state, element, loop, childNodeId, index);

        state = UpdateLoop(state, loop with { NextIndex = instanceCount });
        return new EvaluationResult(state);
    }

    /// <summary>
    /// Reads a collection-mode host's collection variable once at loop start (spec 123 D2), returning the
    /// per-instance item snapshot (in array order) or a deterministic fault: unreadable (variable not visible —
    /// defensive, unreachable post-validation), non-array present value, or an externally-stored (non-inline)
    /// payload (stated cut). A null/absent collection resolves to an empty snapshot, which the caller routes as
    /// an immediate <c>N == 0</c> completion.
    /// </summary>
    private static (BpmnExecutionState State, IReadOnlyList<JsonElement>? Items, ActivityFault? Fault) ResolveCollectionInstances(
        IRuntimeActivityExecutionContext context,
        BpmnExecutionState state,
        BpmnElement element,
        BpmnLoopCharacteristics loopCharacteristics)
    {
        var variableName = loopCharacteristics.CollectionVariable!;
        if (!context.TryReadScopedVariableValue(variableName, out var envelope) || envelope is null)
        {
            var message = $"BPMN multi-instance element '{element.ElementId}' could not read collection variable '{variableName}'; it is not a visible declared container-scoped variable of the process.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, element.ElementId, null, null, message);
            return (state, null, new ActivityFault(CollectionUnreadableFaultCode, message));
        }

        // null / absent → an empty loop (N == 0): complete immediately and route outbound (spec 123 D2).
        if (envelope.Presence is ValuePresence.Absent or ValuePresence.ExplicitNull)
            return (state, [], null);

        // A present value stored externally is a stated cut — resolving it needs machinery beyond the inline read.
        if (envelope.InlineValue is not { } inline)
        {
            var message = $"BPMN multi-instance element '{element.ElementId}' collection variable '{variableName}' holds an externally-stored value; only an inline collection is executable in this slice.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, element.ElementId, null, null, message);
            return (state, null, new ActivityFault(CollectionNotInlineFaultCode, message));
        }

        if (inline.ValueKind != JsonValueKind.Array)
        {
            var message = $"BPMN multi-instance element '{element.ElementId}' collection variable '{variableName}' is a {inline.ValueKind} value, not a collection; a collection-mode loop requires an array.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, element.ElementId, null, null, message);
            return (state, null, new ActivityFault(CollectionNotACollectionFaultCode, message));
        }

        var items = inline.EnumerateArray().Select(item => item.Clone()).ToArray();
        return (state, items, null);
    }

    /// <summary>
    /// Schedules one multi-instance instance (spec 121 D2b): mints an <c>AwaitingChild</c> instance sub-token
    /// (parented to the coordinator, at the host element) and schedules the bound child on it with a
    /// per-iteration frame seeding <c>loopIndex</c>. The child is scheduled with the process's OWN aei so the
    /// iteration-frame ownership guard holds (<c>SchedulingActivityExecutionId == ParentActivityExecutionId ==
    /// owner-node aei</c>), and its deterministically minted iteration id (derived from the Sequence-based
    /// instance token id) is carried on the provenance and the active-child record.
    /// </summary>
    private static BpmnExecutionState ScheduleMultiInstanceInstance(
        IRuntimeActivityExecutionContext context,
        BpmnExecutionState state,
        BpmnElement element,
        BpmnLoopState loop,
        string childNodeId,
        int index)
    {
        var ownerActivityExecutionId = context.ActivityExecutionState.Execution.ActivityExecutionId;
        // spec 122: an instance sub-token inherits the coordinator token's loop-iteration key, so a
        // multi-instance host revisited across loop passes keeps each pass's instances in their own iteration.
        var coordinatorIterationKey = GetRequiredToken(state, loop.TokenId).IterationKey;
        var instanceToken = NewToken(state, element.ElementId, flowId: null, parentTokenId: loop.TokenId, BpmnTokenStatus.AwaitingChild, producingActivityExecutionId: ownerActivityExecutionId, iterationKey: coordinatorIterationKey);
        state = AddToken(state, instanceToken);

        // spec 123 D2: in collection mode the instance's item comes from the loop-start snapshot on the record
        // (never re-reading the variable — snapshot discipline), seeded under the host's ItemVariable.
        var item = loop.Items is { } items && index < items.Count ? items[index] : (JsonElement?)null;
        var itemVariable = element.LoopCharacteristics!.IsCollectionMode ? element.LoopCharacteristics.ItemVariable : null;

        var iterationId = MultiInstanceIterationIdPrefix + instanceToken.TokenId;
        var frame = BuildMultiInstanceIterationFrame(context, iterationId, index, item, itemVariable);
        state = BpmnScheduler.ScheduleChild(context, state, childNodeId, element.ElementId, instanceToken.TokenId, ownerActivityExecutionId, MultiInstanceSchedulingCause, frame, iterationId);

        return BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Scheduled, element.ElementId, null, instanceToken.TokenId,
            $"BPMN multi-instance element '{element.ElementId}' scheduled instance {index} (child '{childNodeId}').");
    }

    /// <summary>
    /// Builds a per-instance iteration frame owned by the <c>BpmnProcess</c> node (spec 121 D2b / spec 123 D2),
    /// seeding a durable zero-based <c>loopIndex</c> and, in collection mode, the current item under the host's
    /// <c>ItemVariable</c> as a durable <c>InstanceInline</c> envelope typed with the canonical dynamic value
    /// type (ADR 0035 <c>Elsa.Any</c>, mirroring how <c>ForEach</c> types a generic item).
    /// </summary>
    private static LoopIterationScopeRequest BuildMultiInstanceIterationFrame(
        IRuntimeActivityExecutionContext context, string iterationId, int index, JsonElement? item, string? itemVariable)
    {
        var values = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal)
        {
            [BpmnLoopCharacteristics.LoopIndexVariable] = ValueEnvelope.Inline(
                new ValueTypeDescriptor("Int32"),
                JsonSerializer.SerializeToElement(index),
                ValueProtectionPolicy.InstanceInline)
        };
        if (itemVariable is not null && item is { } itemValue)
        {
            values[itemVariable] = ValueEnvelope.Inline(
                new ValueTypeDescriptor("Elsa.Any"),
                itemValue,
                ValueProtectionPolicy.InstanceInline);
        }
        return new LoopIterationScopeRequest(context.ExecutableNode.ExecutableNodeId, iterationId, values);
    }

    /// <summary>
    /// Handles a multi-instance instance completion (spec 121 D2c): consumes the instance sub-token and advances
    /// the loop. While instances remain, sequential mode schedules the next instance and parallel mode simply
    /// records the completion. On the LAST instance the loop record is dropped, the coordinator's still-armed
    /// catch listeners are torn down (spec 120 host-completion semantics), and the coordinator routes the host
    /// element's outbound flows through its normal behavior — so a multi-instance host routes exactly once.
    /// </summary>
    private EvaluationResult HandleMultiInstanceInstanceCompletion(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken instanceToken,
        BpmnLoopState loop,
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations,
        string completedActivityExecutionId)
    {
        state = UpdateToken(state, instanceToken with { Status = BpmnTokenStatus.Consumed });
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Consumed, loop.ElementId, null, instanceToken.TokenId,
            $"BPMN multi-instance element '{loop.ElementId}' instance token '{instanceToken.TokenId}' completed.");

        var completedCount = loop.CompletedCount + 1;
        if (completedCount < loop.TotalCount)
        {
            if (loop.IsSequential && loop.NextIndex < loop.TotalCount)
            {
                var hostElement = graph.GetRequiredElement(loop.ElementId);
                state = ScheduleMultiInstanceInstance(context, state, hostElement, loop, hostElement.ChildNodeId!, loop.NextIndex);
                state = UpdateLoop(state, loop with { NextIndex = loop.NextIndex + 1, CompletedCount = completedCount });
            }
            else
            {
                state = UpdateLoop(state, loop with { CompletedCount = completedCount });
            }

            return new EvaluationResult(state, PendingSubtreeCancellations: pendingCancellations);
        }

        // Last instance: the loop is done — the coordinator "completes" and routes.
        state = RemoveLoop(state, loop.LoopId);
        var coordinator = GetRequiredToken(state, loop.TokenId);
        state = ApplyBoundaryCompletionSemantics(graph, state, coordinator, liveChildAeiByNode, pendingCancellations);

        var result = RouteMultiInstanceCoordinatorOutbound(context, graph, state, graph.GetRequiredElement(loop.ElementId), coordinator, completedActivityExecutionId);
        return result with { PendingSubtreeCancellations = pendingCancellations };
    }

    /// <summary>Routes a multi-instance coordinator's outbound flows through the host element's normal behavior (spec 121 D2c/D2a): task-flow selection with no outcome names, faulting <c>bpmn.flow.none-taken</c> when nothing matches, exactly as a single-run host.</summary>
    private EvaluationResult RouteMultiInstanceCoordinatorOutbound(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnElement element,
        BpmnToken coordinatorToken,
        string schedulingActivityExecutionId)
    {
        var behavior = behaviorRegistry.GetRequired(BpmnElementFamilies.Resolve(element));
        var behaviorContext = new BpmnBehaviorContext(
            BpmnBehaviorTrigger.ChildCompleted,
            element,
            coordinatorToken,
            graph.OutboundFlows(element.ElementId),
            graph.InboundFlows(element.ElementId),
            [],
            state);

        var result = ApplyDecision(context, graph, state, coordinatorToken, element, Execute(behavior, behaviorContext, element), schedulingActivityExecutionId);
        if (result is { Fault: null, Terminated: false })
            result = Propagate(context, graph, result.State, schedulingActivityExecutionId);
        return result;
    }

    private sealed record EvaluationResult(
        BpmnExecutionState State,
        ActivityFault? Fault = null,
        bool Terminated = false,
        IReadOnlyCollection<BpmnPendingSubtreeCancellation>? PendingSubtreeCancellations = null,
        BpmnFaultAbsorption? FaultAbsorption = null,
        BpmnPendingParentNotification? PendingParentNotification = null);

    /// <summary>
    /// One live armed child subtree to be torn down via seam A (spec 119 event-based-gateway losers and spec
    /// 120 boundary host/listener teardown), carried on the evaluation result and staged only on a clean
    /// (non-fault) continuation. <see cref="Reason"/> is the per-cancellation seam-A reason.
    /// </summary>
    private sealed record BpmnPendingSubtreeCancellation(string ActivityExecutionId, string ElementId, string Reason);

    /// <summary>The seam-B fault absorption an error boundary staged for a child-fault evaluation (spec 120); staged only on a clean (non-fault) continuation.</summary>
    private sealed record BpmnFaultAbsorption(string IncidentId, string Reason);

    /// <summary>The seam-C parent notification a bubbling escalation re-stages (spec 127): an escalation unmatched at this scope is re-raised verbatim to the grandparent. Staged only on a clean (non-fault) continuation.</summary>
    private sealed record BpmnPendingParentNotification(string Code, JsonElement? Payload);

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
        // spec 128 D3: an own-scope escalation match is captured here and activated AFTER the full throw decision
        // (its companion routing command runs first), so an interrupting activation's stop-others also stops the
        // throw's just-emitted successor.
        BpmnEventSubprocessCatcher? ownScopeActivation = null;

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

                    state = EmitFlowTokens(state, graph, element, token, command.FlowIds, schedulingActivityExecutionId, memberTokenIds);

                    if (memberTokenIds is { Count: > 0 })
                        state = AddRace(state, element.ElementId, memberTokenIds);

                    break;
                }
                case BpmnBehaviorCommandKind.TriggerCompensation:
                {
                    // spec 124: the compensate throw/end token requests a compensation replay. TriggerCompensation
                    // is the sole command a compensate behavior emits, so the resolved result is returned directly.
                    return TriggerCompensation(context, graph, state, token, element, schedulingActivityExecutionId);
                }
                case BpmnBehaviorCommandKind.CancelTransaction:
                {
                    // spec 125: the cancel end token requests the transaction cancellation. CancelTransaction is the
                    // sole command a cancel-end behavior emits, so the resolved result is returned directly.
                    return CancelTransaction(context, graph, state, token, element, schedulingActivityExecutionId);
                }
                case BpmnBehaviorCommandKind.RaiseEscalation:
                {
                    // spec 127/128: the escalation throw/end raises an escalation. RaiseEscalation is the FIRST command
                    // of the throw's decision ([RaiseEscalation, EmitTokens|ConsumeToken]); it either records an
                    // own-scope catch (spec 128 — activated after the loop), stages a seam-C parent notification, or
                    // records a root no-op diagnostic (never a fault), and the loop then processes the companion
                    // routing command as usual.
                    var raised = RaiseEscalation(context, graph, state, token, element);
                    state = raised.State;
                    ownScopeActivation = raised.OwnScopeActivation;
                    break;
                }
                case BpmnBehaviorCommandKind.ScheduleChild:
                {
                    if (element.ChildNodeId is not { } childNodeId)
                        throw new BpmnExecutionException($"BPMN behavior for element '{element.ElementId}' requested a child schedule, but the element binds no child activity.");

                    graph.GetRequiredChildNode(childNodeId);

                    // spec 121: a multi-instance host does not schedule its bound child on the arriving token —
                    // the token becomes a loop coordinator and the engine runs the child N times on private
                    // per-instance sub-tokens. This is the only command a task/subprocess behavior emits, so
                    // the loop-start result is returned directly.
                    if (element.LoopCharacteristics is not null)
                        return StartMultiInstanceLoop(context, graph, state, element, token, childNodeId, schedulingActivityExecutionId);

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

        // spec 128 D3/D5: activate the own-scope event subprocess now that the throw's routing command has run. This
        // evaluation rides the Propagate loop, so an interrupting activation stops other live work logically only
        // (NoLiveChildren — the CancelLiveWork-logical-only invariant); in-flight children are absorbed on late
        // completion (the transaction-cancel precedent).
        if (ownScopeActivation is { } catcher)
        {
            var ownScopeCancellations = new List<BpmnPendingSubtreeCancellation>();
            return ActivateEventSubprocess(context, graph, state, catcher, token.IterationKey, NoLiveChildren, ownScopeCancellations,
                schedulingActivityExecutionId, $"own-scope escalation code '{ReadEscalation(element).Code}'");
        }

        return new EvaluationResult(state);
    }

    /// <summary>
    /// Mints one token per listed outbound flow from <paramref name="element"/> (spec 119/122): a join target
    /// parks the token <c>WaitingAtJoin</c>, a backward (loop-back) edge mints a fresh iteration key, and every
    /// other edge inherits the emitting token's key. Shared by the <c>EmitTokens</c> command (which additionally
    /// collects the minted ids into <paramref name="memberTokenIds"/> for an event-based-gateway race) and the
    /// spec 124 compensate throw's outbound routing (<paramref name="memberTokenIds"/> null — a throw is never a
    /// gateway). The caller consumes the emitting token.
    /// </summary>
    private static BpmnExecutionState EmitFlowTokens(
        BpmnExecutionState state,
        BpmnGraph graph,
        BpmnElement element,
        BpmnToken token,
        IReadOnlyCollection<string> flowIds,
        string schedulingActivityExecutionId,
        List<string>? memberTokenIds)
    {
        foreach (var flowId in flowIds)
        {
            var flow = graph.GetRequiredFlow(flowId);
            if (!StringComparer.Ordinal.Equals(flow.SourceRef, element.ElementId))
                throw new BpmnExecutionException($"BPMN behavior for element '{element.ElementId}' emitted a token on flow '{flowId}', which does not originate from it.");

            var status = BpmnTokenCoordinator.ShouldWaitAtJoin(graph, flow.TargetRef) ? BpmnTokenStatus.WaitingAtJoin : BpmnTokenStatus.Active;
            var iterationKey = graph.IsBackwardFlow(flow.FlowId)
                ? NewIterationKey(state, flow.TargetRef)
                : token.IterationKey;
            var emitted = NewToken(state, flow.TargetRef, flow.FlowId, token.TokenId, status, schedulingActivityExecutionId, iterationKey);
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

        return state;
    }

    /// <summary>
    /// Handles a <c>TriggerCompensation</c> command (spec 124 D2b): selects the target <c>Registered</c>
    /// compensables (all of them, or only the <c>activityRef</c> host's) in reverse registration order and claims
    /// them atomically in this evaluation. An empty selection completes the throw immediately (route/consume — no
    /// fault, no run). A non-empty selection parks the throw token as the run coordinator (<c>AwaitingChild</c>),
    /// writes the run record, and schedules the first handler; the remaining handlers run one at a time, driven by
    /// each handler completion's interception.
    /// </summary>
    private EvaluationResult TriggerCompensation(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken throwToken,
        BpmnElement throwElement,
        string schedulingActivityExecutionId)
    {
        var activityRef = ReadCompensationActivityRef(throwElement);

        // Reverse registration order = reverse completion order (newest-first). Compensables are appended in
        // registration order, so reversing the Registered subset yields the replay order; the claim flips are
        // committed within this one evaluation (a concurrent parallel-branch throw sees only still-Registered
        // records, so no compensable is ever claimed twice).
        var claimed = state.Compensables
            .Where(compensable => compensable.Status == BpmnCompensableStatus.Registered
                                  && (activityRef is null || StringComparer.Ordinal.Equals(compensable.HostElementId, activityRef)))
            .Reverse()
            .ToArray();

        if (claimed.Length == 0)
        {
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.CompensationTriggered, throwElement.ElementId, null, throwToken.TokenId,
                $"BPMN compensate event '{throwElement.ElementId}' had nothing to compensate{(activityRef is null ? "" : $" for '{activityRef}'")}; completing immediately.");
            return CompleteThrow(context, graph, state, throwToken, throwElement, schedulingActivityExecutionId);
        }

        foreach (var compensable in claimed)
            state = UpdateCompensable(state, compensable with { Status = BpmnCompensableStatus.Claimed });

        state = UpdateToken(state, throwToken with { Status = BpmnTokenStatus.AwaitingChild });
        var (stateWithRun, run) = AddCompensationRun(state, throwToken.TokenId, claimed.Select(compensable => compensable.CompensableId).ToArray());
        state = stateWithRun;
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.CompensationTriggered, throwElement.ElementId, null, throwToken.TokenId,
            $"BPMN compensate event '{throwElement.ElementId}' opened run '{run.RunId}' claiming {claimed.Length} compensable(s) (reverse registration order).");

        state = ScheduleNextCompensationHandler(context, graph, state, run);
        return new EvaluationResult(state);
    }

    /// <summary>
    /// Handles a <c>CancelTransaction</c> command (spec 125 D2a): a cancel end event fired inside a transaction
    /// scope. First it stops all OTHER live work logically — every live token except the cancel-end token is
    /// cancelled through <see cref="CancelTokenAndChild"/> so the MI/race/compensation-run coordinator cascades
    /// tear loops/races/replays down consistently — while in-flight children keep running and are absorbed on late
    /// completion (the terminate precedent; logical-only, no seam-A staging). It records the <c>Cancelling</c>
    /// verdict, then claims every <c>Registered</c> compensable (the whole scope) in reverse registration order and
    /// opens a spec-124 <see cref="BpmnCompensationRun"/> coordinated by the cancel-end token, scheduling the first
    /// handler. An empty claim consumes the cancel-end token immediately; <see cref="FinishEvaluation"/> then
    /// completes the process with the <c>Cancelled</c> outcome once nothing is live.
    /// </summary>
    private EvaluationResult CancelTransaction(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken cancelEndToken,
        BpmnElement cancelEndElement,
        string schedulingActivityExecutionId)
    {
        // Step 1: stop all OTHER live work logically. The cancel-end token stays live as the coming replay's
        // coordinator; every other live token is cancelled (cascading MI/race/compensation-run teardown). In-flight
        // children keep running (empty live-child map → no seam-A staging), absorbed on late completion. The
        // stop-others loop is shared with the spec 128 event-subprocess interrupting activation (StopOtherLiveWork).
        var stopScratch = new List<BpmnPendingSubtreeCancellation>();
        var (stoppedState, stoppedCount) = StopOtherLiveWork(state, cancelEndToken.TokenId, TransactionCancelledStopReason, NoLiveChildren, stopScratch,
            (token, reason) => $"BPMN cancel end event '{cancelEndElement.ElementId}' stopped live token '{token.TokenId}' at '{token.AtElementId}' ({reason}).");
        state = stoppedState;

        state = state with { Cancelling = true, Sequence = state.Sequence + 1 };
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.TransactionCancelled, cancelEndElement.ElementId, null, cancelEndToken.TokenId,
            $"BPMN cancel end event '{cancelEndElement.ElementId}' began cancelling the transaction (stopped {stoppedCount} other live token(s)).");

        // Step 2: claim every Registered compensable (the whole scope, reverse registration order) and open a
        // compensation run coordinated by the cancel-end token. An empty claim skips straight to completion.
        var claimed = state.Compensables
            .Where(compensable => compensable.Status == BpmnCompensableStatus.Registered)
            .Reverse()
            .ToArray();

        if (claimed.Length == 0)
        {
            state = UpdateToken(state, cancelEndToken with { Status = BpmnTokenStatus.Consumed });
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.TransactionCancelled, cancelEndElement.ElementId, null, cancelEndToken.TokenId,
                $"BPMN cancel end event '{cancelEndElement.ElementId}' had nothing to compensate; completing the transaction with the '{CancelledOutcomeName}' outcome.");
            return new EvaluationResult(state);
        }

        foreach (var compensable in claimed)
            state = UpdateCompensable(state, compensable with { Status = BpmnCompensableStatus.Claimed });

        state = UpdateToken(state, cancelEndToken with { Status = BpmnTokenStatus.AwaitingChild });
        var (stateWithRun, run) = AddCompensationRun(state, cancelEndToken.TokenId, claimed.Select(compensable => compensable.CompensableId).ToArray());
        state = stateWithRun;
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.CompensationTriggered, cancelEndElement.ElementId, null, cancelEndToken.TokenId,
            $"BPMN cancel end event '{cancelEndElement.ElementId}' opened run '{run.RunId}' claiming {claimed.Length} compensable(s) (reverse registration order).");

        state = ScheduleNextCompensationHandler(context, graph, state, run);
        return new EvaluationResult(state);
    }

    /// <summary>
    /// Schedules the head pending handler of a compensation run (spec 124 D2c): mints an <c>AwaitingChild</c>
    /// sub-token at the handler element (parented to the coordinating throw token, inheriting its iteration key)
    /// and schedules the handler element's bound child on it with the process's own aei (no iteration frame this
    /// slice). One sub-token + one child schedule per handler; the run replays sequentially.
    /// </summary>
    private static BpmnExecutionState ScheduleNextCompensationHandler(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnCompensationRun run)
    {
        var headCompensableId = run.PendingCompensableIds[0];
        var compensable = state.Compensables.First(candidate => StringComparer.Ordinal.Equals(candidate.CompensableId, headCompensableId));
        var handlerElement = graph.GetRequiredElement(compensable.HandlerElementId);
        var throwToken = GetRequiredToken(state, run.ThrowTokenId);
        var ownerActivityExecutionId = context.ActivityExecutionState.Execution.ActivityExecutionId;

        var handlerToken = NewToken(state, handlerElement.ElementId, flowId: null, parentTokenId: run.ThrowTokenId, BpmnTokenStatus.AwaitingChild,
            producingActivityExecutionId: ownerActivityExecutionId, iterationKey: throwToken.IterationKey);
        state = AddToken(state, handlerToken);
        state = BpmnScheduler.ScheduleChild(context, state, handlerElement.ChildNodeId!, handlerElement.ElementId, handlerToken.TokenId, ownerActivityExecutionId, CompensationSchedulingCause);
        return BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Scheduled, handlerElement.ElementId, null, handlerToken.TokenId,
            $"BPMN compensation run '{run.RunId}' scheduled handler '{handlerElement.ElementId}' (child '{handlerElement.ChildNodeId}') for compensable '{headCompensableId}'.");
    }

    /// <summary>
    /// Advances a compensation run when its head handler's sub-token completes (spec 124 D2c). Consumes the
    /// sub-token, flips the head compensable to <c>Compensated</c>, then either schedules the next pending handler
    /// or — when none remain — drops the run record and completes the coordinating throw (route outbound for an
    /// intermediate throw, consume for a compensate end). The handler completion was intercepted before behavior
    /// dispatch, so the sub-token never routes boundary/host semantics.
    /// </summary>
    private EvaluationResult HandleCompensationHandlerCompletion(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken handlerToken,
        BpmnCompensationRun run,
        string completedActivityExecutionId)
    {
        state = UpdateToken(state, handlerToken with { Status = BpmnTokenStatus.Consumed });

        var headCompensableId = run.PendingCompensableIds[0];
        var compensable = state.Compensables.First(candidate => StringComparer.Ordinal.Equals(candidate.CompensableId, headCompensableId));
        state = UpdateCompensable(state, compensable with { Status = BpmnCompensableStatus.Compensated });
        state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Compensated, handlerToken.AtElementId, null, handlerToken.TokenId,
            $"BPMN compensation run '{run.RunId}' compensated '{headCompensableId}' (handler '{handlerToken.AtElementId}').");

        var remaining = run.PendingCompensableIds.Skip(1).ToArray();
        if (remaining.Length > 0)
        {
            state = UpdateCompensationRun(state, run with { PendingCompensableIds = remaining });
            var advanced = FindCompensationRun(state, run.ThrowTokenId)!;
            state = ScheduleNextCompensationHandler(context, graph, state, advanced);
            return new EvaluationResult(state);
        }

        // Last handler: drop the run and complete the coordinating throw.
        state = RemoveCompensationRun(state, run.RunId);
        var throwToken = GetRequiredToken(state, run.ThrowTokenId);
        var throwElement = graph.GetRequiredElement(throwToken.AtElementId);

        // spec 125: a cancel-end coordinator does NOT route/consume like a compensate throw/end — it completes the
        // whole process with the Cancelled outcome. Consume the cancel-end token; FinishEvaluation sees Cancelling
        // set with nothing live and stages Complete("Cancelled").
        if (BpmnElementFamilies.IsCancelEndEvent(throwElement))
        {
            state = UpdateToken(state, throwToken with { Status = BpmnTokenStatus.Consumed });
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.TransactionCancelled, throwElement.ElementId, null, throwToken.TokenId,
                $"BPMN cancel end event '{throwElement.ElementId}' finished replaying the transaction's compensables; completing with the '{CancelledOutcomeName}' outcome.");
            return new EvaluationResult(state);
        }

        var result = CompleteThrow(context, graph, state, throwToken, throwElement, completedActivityExecutionId);
        if (result is { Fault: null, Terminated: false })
            result = Propagate(context, graph, result.State, completedActivityExecutionId);
        return result;
    }

    /// <summary>
    /// Completes a compensate throw/end token once its replay finished or found nothing to claim (spec 124 D2b):
    /// a compensate end consumes its token (none-end semantics); a compensate intermediate throw routes its
    /// outbound flows through normal task-flow selection (<c>bpmn.flow.none-taken</c> applies as usual).
    /// </summary>
    private EvaluationResult CompleteThrow(
        IRuntimeActivityExecutionContext context,
        BpmnGraph graph,
        BpmnExecutionState state,
        BpmnToken throwToken,
        BpmnElement throwElement,
        string schedulingActivityExecutionId)
    {
        if (StringComparer.Ordinal.Equals(throwElement.ElementType, BpmnElementTypes.EndEvent))
        {
            state = UpdateToken(state, throwToken with { Status = BpmnTokenStatus.Consumed });
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Consumed, throwElement.ElementId, null, throwToken.TokenId,
                $"BPMN compensate end event '{throwElement.ElementId}' consumed token '{throwToken.TokenId}' after the compensation replay.");
            return new EvaluationResult(state);
        }

        var behaviorContext = new BpmnBehaviorContext(
            BpmnBehaviorTrigger.ChildCompleted, throwElement, throwToken,
            graph.OutboundFlows(throwElement.ElementId), graph.InboundFlows(throwElement.ElementId), [], state);
        var flows = BpmnFlowSelector.SelectTaskFlows(behaviorContext);
        if (flows.Count == 0 && behaviorContext.OutboundFlows.Count > 0)
        {
            var message = $"BPMN compensate throw event '{throwElement.ElementId}' completed its replay but no outbound sequence flow matched and no default flow is declared.";
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Faulted, throwElement.ElementId, null, throwToken.TokenId, message);
            return new EvaluationResult(state, new ActivityFault("bpmn.flow.none-taken", message));
        }

        state = UpdateToken(state, throwToken with { Status = BpmnTokenStatus.Consumed });
        state = EmitFlowTokens(state, graph, throwElement, throwToken, BpmnFlowSelector.FlowIds(flows), schedulingActivityExecutionId, memberTokenIds: null);
        return new EvaluationResult(state);
    }

    /// <summary>The <c>activityRef</c> a compensate throw/end targets (spec 124), or <c>null</c> when it compensates every registration.</summary>
    private static string? ReadCompensationActivityRef(BpmnElement element) =>
        element.EventDefinitions.SingleOrDefault() is { } definition
        && definition.Properties.TryGetValue(BpmnEventDefinitionProperties.ActivityRef, out var value)
        && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

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

        // spec 134 D3: liveness is computed over NON-listener tokens and NON-listener active children (a listener token
        // is Kind==Listener; a listener's active child is the child scheduled on that token). A scope listener must never
        // block the scope from completing, and excluding a listener's active child keeps the deadlock detector from
        // misfiring on a listener-plus-real-join state (the listener's excluded child would otherwise make
        // ActiveChildren.Count == 0 while a genuine join-waiter sits there). The completion check AND the deadlock
        // detector use the SAME filtered view — patched together. Teardown/stop sites stay unfiltered (listeners die
        // with the scope).
        var listenerTokenIds = state.Tokens
            .Where(token => token.Kind == BpmnTokenKind.Listener)
            .Select(token => token.TokenId)
            .ToHashSet(StringComparer.Ordinal);
        var nonListenerLiveTokens = liveTokens.Where(token => !listenerTokenIds.Contains(token.TokenId)).ToArray();
        var nonListenerActiveChildCount = state.ActiveChildren.Count(child => !listenerTokenIds.Contains(child.TokenId));

        if (nonListenerLiveTokens.Length == 0 && nonListenerActiveChildCount == 0)
        {
            // Only scope listeners (if any) remain live: the real work is done. Strictly AFTER the fault/pending-fault/
            // terminate branches above (which own their own teardown via CancelLiveWork), tear the still-armed listeners
            // down — CancelTokenAndChild carries each live durable listener child's seam-A cancellation, folded into this
            // commit's staged cancellations, in token-id ordinal order — then complete with the normal outcome selection
            // (spec 134 D3 teardown-then-complete).
            var liveListenerTokenIds = liveTokens
                .Where(token => listenerTokenIds.Contains(token.TokenId))
                .Select(token => token.TokenId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            if (liveListenerTokenIds.Length > 0)
            {
                var pending = new List<BpmnPendingSubtreeCancellation>(result.PendingSubtreeCancellations ?? []);
                var liveChildAeiByNode = BuildLiveChildAeiByNode(context);
                foreach (var listenerTokenId in liveListenerTokenIds)
                {
                    var listenerElementId = GetRequiredToken(state, listenerTokenId).AtElementId;
                    state = CancelTokenAndChild(state, listenerTokenId, EventSubprocessListenerSupersededByCompletionReason, liveChildAeiByNode, pending,
                        (token, reason) => $"BPMN scope listener token '{token.TokenId}' at '{token.AtElementId}' was retired because the scope completed ({reason}).");
                    state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.ScopeListenerRetired, listenerElementId, null, listenerTokenId,
                        $"BPMN event subprocess '{listenerElementId}' scope listener '{listenerTokenId}' was retired when the scope completed.");
                }
                result = result with { State = state, PendingSubtreeCancellations = pending };
            }

            // Clean completion — legal to stage the carried seam-A/seam-B teardown in this commit (spec 119 D3 / spec 120 D5 / spec 134 D3).
            StagePendingSubtreeCancellations(context, result);
            // spec 125: a cancelled transaction completes with the distinguishable Cancelled outcome (parent maps it
            // to the cancel boundary; a root transaction completes the workflow with it) rather than Done.
            var outcomeName = state.Cancelling ? CancelledOutcomeName : ActivityOutcomes.Done;
            state = BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Completed, null, null, null,
                $"BPMN process completed with the '{outcomeName}' outcome because no live tokens remain.");
            return persister.StageState(RuntimeStructuralContinuation.Complete(outcomeName), state);
        }

        // A parked join arrival with nothing live left to produce the missing arrivals can never fire
        // (e.g. a parallel join downstream of an exclusive decision). Fault deterministically instead of
        // leaving the process Running forever. spec 122: iteration-key join grouping does not weaken this —
        // "all live tokens WaitingAtJoin, no active children" means nothing can propagate a new arrival for
        // ANY iteration group, so it is a true deadlock regardless of how arrivals are grouped. spec 134 D3: the
        // filtered view keeps a lone armed listener (which will never produce a join arrival) from being read as a
        // deadlock while preserving a genuine join-deadlock alongside a live listener.
        if (nonListenerActiveChildCount == 0 && nonListenerLiveTokens.Length > 0 && nonListenerLiveTokens.All(token => token.Status == BpmnTokenStatus.WaitingAtJoin))
        {
            var joinElementIds = string.Join(", ", nonListenerLiveTokens.Select(token => token.AtElementId).Distinct(StringComparer.Ordinal));
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
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
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
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
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

        // Case B — the completing token is a boundary host.
        // spec 124: a successful host completion carrying an attached compensation boundary registers one
        // Registered compensable (per completion — a host completed on multiple loop passes registers once per
        // pass, each compensated independently in reverse completion order). Only successful completions reach
        // Case B; the fault/cancel paths never do.
        if (graph.AttachedCompensationBoundary(completingElement.ElementId) is { CompensationHandlerElementId: { } handlerElementId })
        {
            var (registeredState, compensable) = AddCompensable(state, completingElement.ElementId, handlerElementId);
            state = BpmnDiagnosticAccumulator.Add(registeredState, BpmnDiagnosticKind.CompensationRegistered, completingElement.ElementId, null, completingToken.TokenId,
                $"BPMN host '{completingElement.ElementId}' completed and registered compensable '{compensable.CompensableId}' (handler '{handlerElementId}').");
        }

        // Tear down the host's still-armed catch listeners.
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
        // spec 122: listeners inherit the host token's loop-iteration key, so a boundary host revisited across
        // loop passes arms a fresh listener per pass under the pass's iteration.
        var hostIterationKey = state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, hostTokenId))?.IterationKey;
        foreach (var boundary in graph.AttachedCatchBoundaries(hostElement.ElementId))
        {
            if (boundary.ChildNodeId is not { } listenerNodeId)
                continue; // validation guarantees a catch boundary binds a listener; defensive.

            var listenerToken = NewToken(state, boundary.ElementId, flowId: null, parentTokenId: hostTokenId, BpmnTokenStatus.AwaitingChild, producingActivityExecutionId: schedulingActivityExecutionId, iterationKey: hostIterationKey);
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
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
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
    /// A token that is already terminal, or whose child is not live, is a benign skip. When the token is a
    /// multi-instance loop coordinator (spec 121), its live instance sub-tokens cascade-cancel first (their
    /// children resolved by <c>(NodeId, IterationId)</c>) and the loop record is dropped, so cancelling a
    /// multi-instance host cancels all of its live instances. Shared by the spec 119 race and the spec 120
    /// boundary teardown.
    /// </summary>
    private static BpmnExecutionState CancelTokenAndChild(
        BpmnExecutionState state,
        string tokenId,
        string reason,
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations,
        Func<BpmnToken, string, string> diagnosticMessage)
    {
        var token = state.Tokens.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, tokenId));
        if (token is null || token.Status is BpmnTokenStatus.Consumed or BpmnTokenStatus.Canceled)
            return state;

        // spec 121: cascade a multi-instance coordinator's cancellation to its live instance sub-tokens.
        if (FindLoopByCoordinator(state, tokenId) is { } loop)
        {
            foreach (var instanceTokenId in InstanceTokenIds(state, loop))
                state = CancelTokenAndChild(state, instanceTokenId, reason, liveChildAeiByNode, pendingCancellations, diagnosticMessage);
            state = RemoveLoop(state, loop.LoopId);
            token = GetRequiredToken(state, tokenId);
        }

        // spec 124: cascade a compensation run coordinator's cancellation to its live handler sub-token, then drop
        // the run and release its unrun Claimed compensables back to Registered (they were never run; a later
        // throw may re-claim them). Mirrors the MI coordinator cascade.
        if (FindCompensationRun(state, tokenId) is { } compensationRun)
        {
            foreach (var handlerTokenId in CompensationHandlerTokenIds(state, tokenId))
                state = CancelTokenAndChild(state, handlerTokenId, reason, liveChildAeiByNode, pendingCancellations, diagnosticMessage);

            foreach (var compensableId in compensationRun.PendingCompensableIds)
                if (state.Compensables.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.CompensableId, compensableId)) is { Status: BpmnCompensableStatus.Claimed } claimed)
                    state = UpdateCompensable(state, claimed with { Status = BpmnCompensableStatus.Registered });

            state = RemoveCompensationRun(state, compensationRun.RunId);
            token = GetRequiredToken(state, tokenId);
        }

        var child = state.ActiveChildren.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TokenId, tokenId));
        state = UpdateToken(state, token with { Status = BpmnTokenStatus.Canceled });
        if (child is not null)
        {
            state = RemoveActiveChild(state, tokenId);
            if (liveChildAeiByNode.TryGetValue((child.NodeId, child.IterationId), out var activityExecutionId))
                pendingCancellations.Add(new BpmnPendingSubtreeCancellation(activityExecutionId, token.AtElementId, reason));
        }

        return BpmnDiagnosticAccumulator.Add(state, BpmnDiagnosticKind.Canceled, token.AtElementId, token.FlowId, token.TokenId, diagnosticMessage(token, reason));
    }

    /// <summary>
    /// Stops all live work in the scope except <paramref name="keepTokenId"/> (spec 125 / spec 128): every other
    /// live token is cancelled through <see cref="CancelTokenAndChild"/> so the MI/race/compensation-run coordinator
    /// cascades tear loops/races/replays down consistently. Shared by the cancel-transaction path (which keeps the
    /// cancel-end coordinator, logical-only via <see cref="NoLiveChildren"/>) and the event-subprocess interrupting
    /// activation (which keeps the activation token). Returns the count of stopped tokens for the caller's diagnostic.
    /// </summary>
    private static (BpmnExecutionState State, int StoppedCount) StopOtherLiveWork(
        BpmnExecutionState state,
        string keepTokenId,
        string reason,
        IReadOnlyDictionary<(string NodeId, string? IterationId), string> liveChildAeiByNode,
        List<BpmnPendingSubtreeCancellation> pendingCancellations,
        Func<BpmnToken, string, string> diagnosticMessage)
    {
        var otherLiveTokenIds = state.Tokens
            .Where(candidate => candidate.Status is BpmnTokenStatus.Active or BpmnTokenStatus.AwaitingChild or BpmnTokenStatus.WaitingAtJoin
                                && !StringComparer.Ordinal.Equals(candidate.TokenId, keepTokenId))
            .Select(candidate => candidate.TokenId)
            .ToArray();
        foreach (var liveTokenId in otherLiveTokenIds)
            state = CancelTokenAndChild(state, liveTokenId, reason, liveChildAeiByNode, pendingCancellations, diagnosticMessage);
        return (state, otherLiveTokenIds.Length);
    }

    /// <summary>The instance sub-token ids of a live multi-instance loop (spec 121): tokens parented to the coordinator and sitting at the host element.</summary>
    private static IReadOnlyCollection<string> InstanceTokenIds(BpmnExecutionState state, BpmnLoopState loop) =>
        state.Tokens
            .Where(candidate =>
                candidate.ParentTokenId is { } parent && StringComparer.Ordinal.Equals(parent, loop.TokenId) &&
                StringComparer.Ordinal.Equals(candidate.AtElementId, loop.ElementId))
            .Select(candidate => candidate.TokenId)
            .ToArray();

    /// <summary>The live handler sub-token ids of a compensation run (spec 124): live tokens parented to the coordinating throw token (its handler sub-tokens sit at handler elements; ordinary emitted successors are consumed, so this resolves the in-flight handler).</summary>
    private static IReadOnlyCollection<string> CompensationHandlerTokenIds(BpmnExecutionState state, string throwTokenId) =>
        state.Tokens
            .Where(candidate =>
                candidate.ParentTokenId is { } parent && StringComparer.Ordinal.Equals(parent, throwTokenId) &&
                candidate.Status is BpmnTokenStatus.Active or BpmnTokenStatus.AwaitingChild or BpmnTokenStatus.WaitingAtJoin)
            .Select(candidate => candidate.TokenId)
            .ToArray();

    /// <summary>
    /// The live direct child executions of this process keyed by <c>(executable node id, iteration id)</c>
    /// (spec 119 D4 + spec 121). Ordinary single-run children key under a <c>null</c> iteration id (preserving
    /// the pre-multi-instance lookup); N concurrent same-node multi-instance children key under their distinct
    /// iteration ids so a teardown resolves the right instance's activity-execution id.
    /// </summary>
    private static IReadOnlyDictionary<(string NodeId, string? IterationId), string> BuildLiveChildAeiByNode(IRuntimeActivityExecutionContext context)
    {
        var liveChildAeiByNode = new Dictionary<(string NodeId, string? IterationId), string>();
        foreach (var live in context.GetLiveChildActivities())
            liveChildAeiByNode[(live.ExecutableNodeId, live.IterationId)] = live.ActivityExecutionId;
        return liveChildAeiByNode;
    }

    /// <summary>Stages each carried seam-A subtree cancellation, the seam-B fault absorption, and the seam-C bubbled parent notification on the runtime context. Only called from a clean (non-fault) continuation (spec 119 D3 / spec 120 D5 / spec 127 D2b).</summary>
    private static void StagePendingSubtreeCancellations(IRuntimeActivityExecutionContext context, EvaluationResult result)
    {
        foreach (var cancellation in result.PendingSubtreeCancellations ?? [])
            context.RequestChildSubtreeCancellation(
                cancellation.ActivityExecutionId,
                cancellation.Reason,
                new Dictionary<string, string> { [ElementIdMetadataKey] = cancellation.ElementId });

        if (result.FaultAbsorption is { } absorption)
            context.RequestChildFaultAbsorption(absorption.IncidentId, absorption.Reason);

        if (result.PendingParentNotification is { } notification)
            context.RequestParentNotification(notification.Code, notification.Payload);
    }

    /// <summary>Cancels every live token and drops the active-child records; in-flight children keep running and their late completions are absorbed by the token-status guard.</summary>
    private static BpmnExecutionState CancelLiveWork(BpmnExecutionState state)
    {
        foreach (var liveToken in state.Tokens.Where(candidate => candidate.Status is BpmnTokenStatus.Active or BpmnTokenStatus.AwaitingChild or BpmnTokenStatus.WaitingAtJoin).ToArray())
            state = UpdateToken(state, liveToken with { Status = BpmnTokenStatus.Canceled });

        return state with { ActiveChildren = [], Sequence = state.Sequence + 1 };
    }

    private static string ResolveTokenId(IRuntimeActivityExecutionContext context, BpmnExecutionState state, string completedNodeId, string? completedIterationId)
    {
        var candidates = state.ActiveChildren
            .Where(child => StringComparer.Ordinal.Equals(child.NodeId, completedNodeId))
            .ToArray();

        // The primary path: engine-scheduled children carry the bpmn.tokenId command metadata on inline completion.
        // The metadata is authoritative when the completing node has NO active children (its child was already torn
        // down by a terminate/cancel — the late-completion case, absorbed by the canceled-token guard) or when it
        // names one of the node's active children. But when the node HAS active children and the metadata names none
        // of them, it is a nested BpmnProcess body's OWN last internal bpmn.tokenId leaked onto the inline completion
        // work item (spec 128 event-subprocess body): that leaked id must not be mistaken for the parent's activation
        // token, so the by-node resolution below owns it.
        if (context.SchedulerWorkItem.CommandMetadata.TryGetValue(TokenIdMetadataKey, out var metadataTokenId)
            && !string.IsNullOrWhiteSpace(metadataTokenId)
            && (candidates.Length == 0 || candidates.Any(child => StringComparer.Ordinal.Equals(child.TokenId, metadataTokenId))))
            return metadataTokenId;

        // A resumed child's completion does not carry the bpmn.tokenId metadata. For N concurrent same-node
        // multi-instance instances (spec 121) the completing child's iteration id disambiguates them; a single
        // candidate resolves unambiguously (the pre-multi-instance by-node fallback).
        if (completedIterationId is not null &&
            candidates.FirstOrDefault(child => StringComparer.Ordinal.Equals(child.IterationId, completedIterationId)) is { } iterationMatch)
            return iterationMatch.TokenId;

        if (candidates.Length == 1)
            return candidates[0].TokenId;

        throw new BpmnExecutionException($"Unable to resolve the BPMN token for completed child node '{completedNodeId}' (iteration '{completedIterationId ?? "none"}').");
    }
}
