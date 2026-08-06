using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The fused-span driver (ADR 0047 D1+D2 / spec 123). Invoked from the <c>ScheduleActivity</c> handler's terminal
/// continuation point when the target is a ReplaySafe leaf inside a live coalescing burst, it runs the <b>start</b> and
/// <b>invoke</b> stages inline in the same dispatch instead of round-tripping their work items through the scheduler
/// queue + drain loop (D1). The schedule handler has already committed the intent-free <c>ActivityScheduled</c>
/// checkpoint and hands over the <c>StartActivity</c> work item; the driver runs the extracted start stage core
/// (committing the intent-free <c>ActivityStarted</c> checkpoint), then dispatches the retained <c>InvokeActivity</c>
/// work item through the <b>existing, unchanged</b> invoke handler inline.
///
/// <para>After the inline invoke, the top-level span pumps the completion cascade inline (D2): it delivers this
/// execution's pending <c>EnqueueSchedulerWork</c> outbox intents and claims + dispatches the resulting overlay work
/// items through the same handlers, in the same FIFO order and with the same custom-before-fallback handler resolution
/// the drain loop uses — eliding only the per-item drain hop. Because the pump reuses the drain loop's exact queue,
/// outbox, and handler machinery on the same coalescing session, its commits are the discrete commits by construction;
/// the byte-identical guardrail and crash-convergence suites are the gate. A fusable successor <c>ScheduleActivity</c>
/// re-enters the D1 pass (the re-entered span is D1-only — the top pump owns the loop, keeping stack depth constant on
/// long chains); anything the pump cannot prove fusable — join (fan-in) successor edges per ADR 0047 resolution #1,
/// child-fault evaluations, non-ReplaySafe parents, the workflow tail (<c>ContinuationScheduling</c>/<c>Checkpoint</c>),
/// bookmark/cancel work — is released back to the overlay for the discrete drain loop. No new command kinds, no new
/// crash-recovery mechanism: only the original <c>ScheduleActivity</c> is ever durably queued for a fused span, so
/// crash redrive is identical to a mid-segment coalescing crash today (research §5, §8.1).</para>
/// </summary>
public sealed class ReplaySafeFusionDriver
{
    private readonly RuntimeReplaySafeFusionOptions _options;
    private readonly WorkflowStartActivitySchedulerWorkHandler _startHandler;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRuntimeCoalescingSessionAccessor? _coalescingSessionAccessor;
    private readonly RuntimeSchedulerDispatchDiagnostics? _diagnostics;
    private readonly IReadOnlyCollection<IReplaySafeSuccessorRoutingProbe> _successorRoutingProbes;
    private readonly List<PendingFusedSpan> _pendingFusedSpans = [];
    private RuntimeSchedulerWorkItem? _pumpedScheduleSource;
    private bool _pumping;

    public ReplaySafeFusionDriver(
        RuntimeReplaySafeFusionOptions options,
        WorkflowStartActivitySchedulerWorkHandler startHandler,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IServiceProvider serviceProvider,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null,
        RuntimeSchedulerDispatchDiagnostics? diagnostics = null,
        IEnumerable<IReplaySafeSuccessorRoutingProbe>? successorRoutingProbes = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(startHandler);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _options = options;
        _startHandler = startHandler;
        _schedulerWorkQueue = schedulerWorkQueue;
        _serviceProvider = serviceProvider;
        _coalescingSessionAccessor = coalescingSessionAccessor;
        _diagnostics = diagnostics;
        _successorRoutingProbes = successorRoutingProbes?.ToArray() ?? [];
    }

    /// <summary>
    /// Whether a fresh <c>ScheduleActivity</c> targeting <paramref name="node"/> should fuse: the toggle is on, the node
    /// is a ReplaySafe typed leaf (never an intrinsic — those keep their durable pre-activation boundary), and a live
    /// coalescing session owns this execution (fusion is a burst-only locality optimization; outside a burst the
    /// spec-109 carrier / Immediate path stands and every hop is discrete).
    /// </summary>
    public bool ShouldFuse(string workflowExecutionId, ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return _options.Enabled
            && node.IntrinsicKind is null
            && node.ActivityContract?.SideEffectProfile == SideEffectProfile.ReplaySafe
            && _coalescingSessionAccessor?.Current is { } session
            && session.AppliesTo(workflowExecutionId);
    }

    /// <summary>
    /// Continues a fused span past the already-committed <c>ActivityScheduled</c>: runs the start stage inline, then
    /// dispatches the invoke stage inline. If the start stage declines to fuse (not a fresh Scheduled ReplaySafe leaf —
    /// e.g. a redelivery), the <c>StartActivity</c> work item is enqueued so the discrete chain handles it; the item is
    /// never dropped and never enqueued on the successful fused path. A top-level span (one entered from the drain
    /// loop, not from the pump itself) then pumps the completion cascade inline (D2).
    /// </summary>
    public async ValueTask ContinueFusedSpanAsync(RuntimeSchedulerWorkItem startWorkItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startWorkItem);

        // A schedule dispatched by the D2 pump must not run its D1 continuation ahead of an already-queued sibling
        // schedule. The discrete queue appends every derived StartActivity after those siblings. Retain that locality
        // here: collect the contiguous schedule cohort, then run all starts before any invokes once the queue prefix is
        // exhausted. No durable state is rewritten; these are the same derived work items the immediate path uses.
        if (_pumping)
        {
            if (_pumpedScheduleSource is { CommandKind: WorkflowExecutionCommandKind.ScheduleActivity })
                _pendingFusedSpans.Add(new PendingFusedSpan(startWorkItem));
            else
                await _schedulerWorkQueue.EnqueueAsync(startWorkItem, cancellationToken);
            return;
        }

        var invokeWorkItem = await _startHandler.ExecuteFusedStartAsync(startWorkItem, _serviceProvider, cancellationToken);
        if (invokeWorkItem is null)
        {
            // Fallback: not a fresh Scheduled ReplaySafe leaf. Hand the StartActivity item to the overlay queue so the
            // discrete StartActivity handler processes it — discrete-equivalent, nothing dropped (research §4).
            await _schedulerWorkQueue.EnqueueAsync(startWorkItem, cancellationToken);
            return;
        }

        _diagnostics?.RecordFusedSpan();

        // Dispatch the invoke stage through the existing, unchanged handler inline (research §8.1). Its terminal commit
        // (completion + parent-completion intent, or suspension / fault / child-scheduling) flows via the overlay outbox
        // exactly as the discrete path.
        var invokeHandler = ResolveHandler(invokeWorkItem);
        await invokeHandler.HandleAsync(invokeWorkItem, cancellationToken);

        await PumpCompletionCascadeAsync(startWorkItem.WorkflowExecutionId, cancellationToken);
    }

    /// <summary>
    /// The D2 inline completion pump: the drain loop's deliver-outbox → next-item → dispatch cycle run inline inside
    /// the original <c>ScheduleActivity</c> dispatch, on the same coalescing overlay queue and outbox, stopping at the
    /// first item it cannot prove belongs to a ReplaySafe single-predecessor cascade. The overlay queue's strict-FIFO
    /// claim contract keeps the drain loop's head claim (the span's originating item) live for the whole span, so the
    /// pump reads past it via the session's peek/consume pair instead of claiming — single-writer-safe because the
    /// session is confined to this drain. A stop leaves the item queued, so the outer drain loop continues exactly
    /// where the pump left off — the item set, FIFO order, handlers, and commits are the discrete path's own.
    /// </summary>
    private async ValueTask PumpCompletionCascadeAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        // Re-entrancy guard: a fused successor dispatched by the pump re-enters ContinueFusedSpanAsync; that nested
        // span is D1-only and the top pump owns the loop (iterative, not recursive, on long hot loops).
        if (_pumping || !_options.Enabled || !_options.FuseCompletionCascade)
            return;

        var outboxProcessor = _serviceProvider.GetService<IRuntimePostCommitOutboxProcessor>();
        if (outboxProcessor is null)
            return;

        var workflowExecutionStateStore = _serviceProvider.GetService<IWorkflowExecutionStateStore>();
        var pauseGate = _serviceProvider.GetService<IWorkflowSchedulerPauseGate>();

        _pumping = true;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The burst is the pump's whole mandate: a boundary that deactivated the session (suspension, terminal,
                // cap-degenerate) ends inline dispatch — the drain loop owns everything after it.
                if (_coalescingSessionAccessor?.Current is not { } session || !session.AppliesTo(workflowExecutionId))
                    break;

                // Deliver this execution's continuation intents into the overlay queue — the same per-cycle delivery the
                // drain orchestrator runs between drainer cycles, so enqueue order (and thus dispatch order) is FIFO
                // identical to the discrete path.
                var outboxResult = await outboxProcessor.ProcessAsync(
                    new RuntimePostCommitOutboxProcessRequest(
                        limit: WorkflowDrainOrchestratorOptions.DefaultOutboxDeliveryBatchSize,
                        workflowExecutionId: workflowExecutionId,
                        intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork),
                    cancellationToken);
                if (outboxResult.FailedCount > 0)
                    break;

                var workItem = await session.PeekNextPumpableOverlayItemAsync(cancellationToken);
                if (workItem is null)
                {
                    if (_pendingFusedSpans.Count > 0)
                    {
                        if (!await FlushPendingFusedCohortAsync(cancellationToken))
                            break;
                        continue;
                    }

                    if (outboxResult.DeliveredCount == 0)
                        break;
                    continue;
                }

                var decision = await ClassifyAsync(workItem, cancellationToken);
                if (!decision.Fuse)
                {
                    if (decision.IsJoinFallback)
                        _diagnostics?.RecordCascadeJoinFallback();
                    break;
                }

                // Pause-gate parity with the drain loop: a hold that would stop the discrete drain stops the pump.
                if (pauseGate is not null && await pauseGate.EvaluateAsync(workItem, cancellationToken) is { CanAdvance: false })
                    break;

                try
                {
                    var handler = ResolveHandler(workItem);
                    _pumpedScheduleSource = workItem.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity
                        ? workItem
                        : null;
                    await handler.HandleAsync(workItem, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // The item stays queued: the outer drain loop redelivers it discretely, where the drainer's fault
                    // capture / poison machinery owns the outcome (redelivery resolves through the existing idempotency
                    // ladder, exactly as a claim-expiry redelivery would).
                    break;
                }
                finally
                {
                    _pumpedScheduleSource = null;
                }

                if (!await session.ConsumePumpedOverlayItemAsync(workItem.WorkItemId, cancellationToken))
                    break;

                _diagnostics?.RecordInlineCascadeDispatch();

                // W5 parity with the drain loop: never dispatch sibling work past a terminal status.
                if (workflowExecutionStateStore is not null)
                {
                    var workflowState = await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
                    if (workflowState is not null && workflowState.Status.IsTerminal())
                        break;
                }
            }
        }
        finally
        {
            try
            {
                // The original durable ScheduleActivity sources remain untouched as crash authority until their
                // successful boundary. A live pump exit instead releases the exact memory-only continuations already
                // derived from them; this is the discrete queue state at the same point in execution.
                if (_pendingFusedSpans.Count > 0)
                    await EnqueuePendingContinuationsAsync(CancellationToken.None);
            }
            finally
            {
                _pumpedScheduleSource = null;
                _pumping = false;
            }
        }
    }

    /// <summary>
    /// Runs a contiguous nested-schedule cohort in the same stage order as the discrete FIFO queue: every
    /// <c>ActivityStarted</c> proposal first, followed by every <c>InvokeActivity</c> dispatch. If any start cannot
    /// remain fused or any stage faults, all not-yet-dispatched continuations are returned to the existing queue.
    /// </summary>
    private async ValueTask<bool> FlushPendingFusedCohortAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Do not remove a span after its start commits Running: the returned InvokeActivity item is now its exact
            // discrete-equivalent continuation and must survive every later failure/cancellation in this cohort.
            foreach (var span in _pendingFusedSpans.ToArray())
            {
                var invoke = await _startHandler.ExecuteFusedStartAsync(span.Start, _serviceProvider, cancellationToken);
                if (invoke is null)
                {
                    await EnqueuePendingContinuationsAsync(CancellationToken.None);
                    return false;
                }

                span.Invoke = invoke;
                _diagnostics?.RecordFusedSpan();
            }

            while (_pendingFusedSpans.Count > 0)
            {
                var span = _pendingFusedSpans[0];
                await ResolveHandler(span.Invoke!).HandleAsync(span.Invoke!, cancellationToken);
                _pendingFusedSpans.RemoveAt(0);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            await EnqueuePendingContinuationsAsync(CancellationToken.None);
            throw;
        }
        catch
        {
            await EnqueuePendingContinuationsAsync(CancellationToken.None);
            return false;
        }
    }

    private async ValueTask EnqueuePendingContinuationsAsync(CancellationToken cancellationToken)
    {
        // Match the discrete FIFO: sibling ScheduleActivity handlers append every StartActivity first; each completed
        // start appends its InvokeActivity after those starts. Remove only after enqueue succeeds so a partial queue
        // failure leaves the current and remaining continuations available to the pump's outer-finally retry.
        foreach (var span in _pendingFusedSpans.Where(x => x.Invoke is null).ToArray())
        {
            await _schedulerWorkQueue.EnqueueAsync(span.Start, cancellationToken);
            _pendingFusedSpans.Remove(span);
        }

        foreach (var span in _pendingFusedSpans.ToArray())
        {
            await _schedulerWorkQueue.EnqueueAsync(span.Invoke!, cancellationToken);
            _pendingFusedSpans.Remove(span);
        }
    }

    private sealed class PendingFusedSpan(RuntimeSchedulerWorkItem start)
    {
        public RuntimeSchedulerWorkItem Start { get; } = start;
        public RuntimeSchedulerWorkItem? Invoke { get; set; }
    }

    private readonly record struct PumpDecision(bool Fuse, bool IsJoinFallback = false)
    {
        public static readonly PumpDecision Fusable = new(true);
        public static readonly PumpDecision Fallback = new(false);
        public static readonly PumpDecision JoinFallback = new(false, true);
    }

    /// <summary>
    /// Decides whether a claimed overlay item is provably part of a ReplaySafe single-predecessor completion cascade.
    /// The decision only changes dispatch locality — a fallback leaves the item for the drain loop, which produces the
    /// identical commits — so every unprovable case defaults to fallback.
    /// </summary>
    private async ValueTask<PumpDecision> ClassifyAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        switch (workItem.CommandKind)
        {
            case WorkflowExecutionCommandKind.CompleteActivity:
                return await ClassifyCompletionAsync(workItem, cancellationToken);
            case WorkflowExecutionCommandKind.ScheduleActivity:
                return await ClassifyScheduleAsync(workItem, cancellationToken);
            default:
                // Bookmarks, checkpoints, cancellation, resume — never part of the fused cascade.
                return PumpDecision.Fallback;
        }
    }

    private async ValueTask<PumpDecision> ClassifyCompletionAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        RuntimeCompleteActivityCommandPayload? payload;
        try
        {
            payload = RuntimeCompleteActivityPayloadMemo.Deserialize(workItem);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return PumpDecision.Fallback;
        }

        switch (payload?.CompletionKind)
        {
            case SchedulerCompletionKind.ActivityCompleted:
                // Pure stage derivation (no commit): raises the parent evaluation or the root continuation item.
                return PumpDecision.Fusable;

            case SchedulerCompletionKind.ParentCompletionEvaluation:
            {
                // Child-fault evaluations keep the discrete cascade (research §4: a fault terminal never fuses onward).
                if (workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.ChildFaulted, out var faulted) &&
                    bool.TryParse(faulted, out var isFaulted) && isFaulted)
                    return PumpDecision.Fallback;

                // Only a ReplaySafe parent composite's evaluation runs inline; External parents fall back (research §4).
                var parentNode = await ResolveNodeAsync(payload.PinnedExecutable.ArtifactId, payload.ExecutableNodeId, cancellationToken);
                return parentNode?.ActivityContract?.SideEffectProfile == SideEffectProfile.ReplaySafe
                    ? PumpDecision.Fusable
                    : PumpDecision.Fallback;
            }

            default:
                // ContinuationScheduling → Checkpoint is the workflow tail: it commits mandatory (possibly terminal)
                // boundaries, which stay on the discrete drain loop.
                return PumpDecision.Fallback;
        }
    }

    private async ValueTask<PumpDecision> ClassifyScheduleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        RuntimeScheduleActivityCommandPayload? payload;
        try
        {
            payload = workItem.Payload?.Deserialize<RuntimeScheduleActivityCommandPayload>();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return PumpDecision.Fallback;
        }

        if (payload is null)
            return PumpDecision.Fallback;

        var node = await ResolveNodeAsync(payload.PinnedExecutable.ArtifactId, payload.ExecutableNodeId, cancellationToken);
        if (node is null || !ShouldFuse(workItem.WorkflowExecutionId, node))
            return PumpDecision.Fallback;

        // Per-successor parent-composite resolution: provenance → parent activity state → parent executable node.
        var parentActivityExecutionId = payload.SchedulingProvenance.ParentActivityExecutionId ?? payload.ParentActivityExecutionId;
        if (parentActivityExecutionId is null)
            return PumpDecision.Fallback;

        var activityExecutionStateStore = _serviceProvider.GetService<IActivityExecutionStateStore>();
        if (activityExecutionStateStore is null)
            return PumpDecision.Fallback;

        var parentState = await activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, parentActivityExecutionId, cancellationToken);
        if (parentState is null)
            return PumpDecision.Fallback;

        var parentNode = await ResolveNodeAsync(payload.PinnedExecutable.ArtifactId, parentState.Execution.ExecutableNodeId, cancellationToken);
        if (parentNode?.ActivityContract?.SideEffectProfile != SideEffectProfile.ReplaySafe)
            return PumpDecision.Fallback;

        // ADR 0047 resolution #1: only a successor edge proven to have at most one inbound connection fuses; a fan-in
        // (join) edge — and any edge no probe can answer for — keeps the discrete cascade.
        foreach (var probe in _successorRoutingProbes)
        {
            if (probe.TryGetInboundCount(parentNode, payload.ExecutableNodeId) is { } inboundCount)
                return inboundCount <= 1 ? PumpDecision.Fusable : PumpDecision.JoinFallback;
        }

        return PumpDecision.Fallback;
    }

    private async ValueTask<ExecutableNode?> ResolveNodeAsync(string artifactId, string executableNodeId, CancellationToken cancellationToken)
    {
        // Burst-cached read (spec 111): inside the pump's live burst this resolves the same immutable pinned artifact
        // the handlers read, so classification adds no durable round-trips.
        var executable = await PinnedExecutableRead.FindAsync(_serviceProvider, artifactId, cancellationToken);
        return executable is not null && executable.NodesById.TryGetValue(executableNodeId, out var node) ? node : null;
    }

    private IWorkflowSchedulerWorkHandler ResolveHandler(RuntimeSchedulerWorkItem workItem)
    {
        // Mirror WorkflowSchedulerDrainer.FindHandler: a real (non-fallback) handler wins over any
        // IFallbackWorkflowSchedulerWorkHandler (e.g. MissingActivityInvocationSchedulerWorkHandler, which faults),
        // so the fused invoke dispatch resolves the same handler the drainer would have dispatched discretely.
        var handlers = _serviceProvider.GetServices<IWorkflowSchedulerWorkHandler>().ToArray();

        foreach (var handler in handlers)
            if (handler is not IFallbackWorkflowSchedulerWorkHandler && handler.CanHandle(workItem))
                return handler;

        foreach (var handler in handlers)
            if (handler is IFallbackWorkflowSchedulerWorkHandler && handler.CanHandle(workItem))
                return handler;

        throw new InvalidOperationException(
            $"No workflow scheduler work handler accepted the fused '{workItem.CommandKind}' work item '{workItem.WorkItemId}'.");
    }
}
