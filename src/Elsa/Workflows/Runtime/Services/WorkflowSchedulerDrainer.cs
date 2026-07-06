using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowSchedulerDrainer : IWorkflowSchedulerDrainer
{
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IReadOnlyCollection<IWorkflowSchedulerWorkHandler> _customHandlers;
    private readonly IReadOnlyCollection<IWorkflowSchedulerWorkHandler> _fallbackHandlers;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowSchedulerPauseGate? _pauseGate;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IRuntimeExecutionPipelineDispatcher? _pipelineDispatcher;
    private readonly IRuntimeFaultCapturePolicy _faultCapturePolicy;
    private readonly IWorkflowSchedulerPoisonStore? _poisonStore;
    private readonly IRuntimeDomainRetryPolicy? _retryPolicy;
    private readonly IWorkflowEngineTracer _tracer;

    /// <summary>
    /// Creates the drainer. RT-8: the seven telescoping constructors collapsed into this single primary constructor —
    /// three required collaborators (<paramref name="schedulerWorkQueue"/>, <paramref name="handlers"/>,
    /// <paramref name="workflowExecutionStateStore"/>) followed by optional collaborators that default to their
    /// no-op/system implementations. The workflow execution state store is <b>required by construction</b> so the W5
    /// terminal-status guard (which stops sibling work once an execution reaches a terminal status) can never be
    /// silently disabled by picking a narrower constructor.
    /// </summary>
    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        TimeProvider? timeProvider = null,
        IWorkflowSchedulerPauseGate? pauseGate = null,
        IRuntimeExecutionPipelineDispatcher? pipelineDispatcher = null,
        IRuntimeFaultCapturePolicy? faultCapturePolicy = null,
        IWorkflowSchedulerPoisonStore? poisonStore = null,
        IRuntimeDomainRetryPolicy? retryPolicy = null,
        IWorkflowEngineTracer? tracer = null)
    {
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);

        _schedulerWorkQueue = schedulerWorkQueue;
        var handlerSnapshot = handlers.ToArray();
        _customHandlers = handlerSnapshot.Where(handler => handler is not IFallbackWorkflowSchedulerWorkHandler).ToArray();
        _fallbackHandlers = handlerSnapshot.Where(handler => handler is IFallbackWorkflowSchedulerWorkHandler).ToArray();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pauseGate = pauseGate;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _pipelineDispatcher = pipelineDispatcher;
        _faultCapturePolicy = faultCapturePolicy ?? new DefaultRuntimeFaultCapturePolicy();
        _poisonStore = poisonStore;
        _retryPolicy = retryPolicy;
        _tracer = tracer ?? NullWorkflowEngineTracer.Instance;
    }

    public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // MS-9: the drain-cycle span wraps the whole method. StartDrainCycle returns null when tracing is inactive, so
        // no allocation and no ambient Activity is introduced; when active, Activity.Current is set for the scope and
        // restored on dispose. This is trace context only — it is not service location and does not touch the fenced
        // peek->pause->dequeue->dispatch sequence below (no new awaits are introduced inside the loop).
        using var activity = _tracer.StartDrainCycle(request);

        var startedAt = _timeProvider.GetUtcNow();
        var results = new List<RuntimeSchedulerWorkItemResult>();
        var remaining = request.MaxWorkItems ?? int.MaxValue;

        // Once the workflow execution reaches a terminal status (Completed/Faulted/Cancelled), any sibling work
        // a parallel fork already enqueued must not run: dispatching it would write post-completion state. The
        // status is read once on entry (covers "already terminal") and re-checked only after a dispatched item
        // completes — the workflow can only become terminal as a result of work dispatched inside this loop
        // (Finish/checkpoint/cancel handlers all run as dispatched scheduler work here), so a per-iteration read
        // would re-load and deserialize the state document needlessly on durable providers. (#293)
        var stoppedOnTerminalStatus = await IsWorkflowTerminatedAsync(request.WorkflowExecutionId, cancellationToken);

        while (remaining > 0 && !stoppedOnTerminalStatus)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextWorkItem = await PeekAsync(request.WorkflowExecutionId, cancellationToken);
            if (nextWorkItem is null)
                break;

            var pauseDecision = await EvaluatePauseAsync(nextWorkItem, cancellationToken);
            if (pauseDecision is { CanAdvance: false })
            {
                results.Add(CreatePausedResult(nextWorkItem, pauseDecision));
                break;
            }

            // Redrive-safe drain (#412 item 3 / "Window C" closure): the peeked head is NOT destructively dequeued
            // before dispatch. It is dispatched in place and only ack-deleted from the durable queue *after* its effect
            // is durable — after a successful handler return, or (on fault) before the poison record is written and any
            // RetryNow re-enqueue. A crash anywhere inside the handler therefore leaves the source item durably queued,
            // so the resumption sweep's backlog discovery (ListPendingWorkflowExecutionIdsAsync) re-drives it and the
            // handler re-runs idempotently (activity-execution status guards + deterministic follow-up work-item ids the
            // idempotent queue absorbs). This replaces the previous load-first-then-delete dequeue, which stranded an
            // activity when a crash fell between the fallback handler's two independent writes (save state, then enqueue
            // the follow-up work item).
            var result = await DispatchAsync(nextWorkItem, request.AmbientServices, cancellationToken);
            results.Add(result);
            remaining--;

            if (result.Status == RuntimeSchedulerWorkItemResultStatus.Faulted)
                break;

            // The dispatched item may have committed a terminal status (e.g. the Finish path). Re-check so the
            // remaining queued siblings are not dequeued on the next iteration.
            stoppedOnTerminalStatus = await IsWorkflowTerminatedAsync(request.WorkflowExecutionId, cancellationToken);
        }

        // The loop only continues past a Completed result, so a terminal stop always coincides with an
        // all-completed drain; flag it as the stop reason.
        var stopReason = stoppedOnTerminalStatus
            ? RuntimeSchedulerDrainStopReason.WorkflowTerminated
            : (RuntimeSchedulerDrainStopReason?)null;

        // MS-9: outcome tags set after the loop, from already-computed values (no pre-computation, no extra work).
        if (activity is not null)
        {
            activity.SetTag(WorkflowEngineTelemetry.DrainItemsProcessedTag, results.Count);
            if (stopReason is { } reason)
                activity.SetTag(WorkflowEngineTelemetry.DrainStopReasonTag, reason.ToString());
        }

        return new RuntimeSchedulerDrainResult(
            workflowExecutionId: request.WorkflowExecutionId,
            startedAt: startedAt,
            completedAt: _timeProvider.GetUtcNow(),
            items: results,
            stopReason: stopReason);
    }

    private async ValueTask<bool> IsWorkflowTerminatedAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        // RT-7: the terminal-status guard reads the state store injected by construction — no AsyncLocal
        // service-location in the drain path. The store is required (RT-8), so there is no null fallback.
        var state = await _workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        return state is not null && state.Status.IsTerminal();
    }

    private async ValueTask<RuntimeSchedulerWorkItemResult> DispatchAsync(RuntimeSchedulerWorkItem workItem, IServiceProvider? ambientServices, CancellationToken cancellationToken)
    {
        IWorkflowSchedulerWorkHandler? handler = null;
        var startedAt = _timeProvider.GetUtcNow();

        // MS-9: the dispatch span nests under the drain-cycle span via Activity.Current. The activity-execution span
        // (Invoke slot) and the checkpoint-commit span both nest under this one when the pipeline runs. Null when
        // tracing is inactive.
        using var activity = _tracer.StartDispatch(workItem);

        try
        {
            handler = FindHandler(workItem);
            activity?.SetTag(WorkflowEngineTelemetry.HandlerNameTag, handler.Name);

            // Move 1 (ADR 0029): route dispatch through the runtime execution pipeline when one is wired, running the
            // handler as the pipeline's inner terminal delegate. When absent, dispatch the handler directly — with only
            // the built-in pass-through middleware registered the two paths are behavior-identical. RT-7: the drain's
            // ambient services flow explicitly into the pipeline dispatcher, which stages them on the dispatch workspace
            // for slot-invoked handlers to read — no AsyncLocal service location in the drain path.
            if (_pipelineDispatcher is not null)
                await _pipelineDispatcher.DispatchAsync(workItem, handler, ambientServices, cancellationToken);
            else
                await handler.HandleAsync(workItem, cancellationToken);

            // Ack-delete: the handler's effect is now durable, so remove the source item from the durable queue. A crash
            // before this point leaves the item queued for idempotent re-drive; a crash after it has nothing left to
            // re-drive (the effect is committed). The TOCTOU tripwire guards the single-writer invariant.
            await AckAsync(workItem, cancellationToken);

            activity?.SetTag(WorkflowEngineTelemetry.OutcomeTag, WorkflowEngineTelemetry.OutcomeCompleted);

            return new RuntimeSchedulerWorkItemResult(
                workItemId: workItem.WorkItemId,
                workflowExecutionId: workItem.WorkflowExecutionId,
                commandKind: workItem.CommandKind,
                status: RuntimeSchedulerWorkItemResultStatus.Completed,
                handlerName: handler.Name,
                startedAt: startedAt,
                completedAt: _timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var faultInfo = _faultCapturePolicy.Capture(exception);
            // Persisted handler-name — this value is written into poison/drain records (RuntimeSchedulerPoisonRecord.HandlerName,
            // RuntimeSchedulerDrain.HandlerName). Do not rename WorkflowSchedulerDrainer without preserving this literal wire value.
            var handlerName = handler?.Name ?? nameof(WorkflowSchedulerDrainer);

            if (activity is not null)
            {
                activity.SetTag(WorkflowEngineTelemetry.OutcomeTag, WorkflowEngineTelemetry.OutcomeFaulted);
                activity.SetStatus(ActivityStatusCode.Error, faultInfo.ExceptionType);
            }

            // Ack-delete BEFORE poison handling (#412 item 3): a *handler fault* is a decided outcome, not a crash — the
            // drainer stays alive and records the item to the poison store. The source item must therefore be removed
            // from the durable queue here, before HandleHandlerCrashAsync records poison / honors a RetryNow re-enqueue;
            // otherwise the still-queued source item would be re-driven by the sweep AND re-enqueued by RetryNow, and a
            // deterministically-poisoning handler would hot-loop (redeliver forever). Ack-on-fault makes poison delivery
            // bounded. (A process crash — as opposed to a handler fault — never reaches this line, so the item stays
            // queued for idempotent re-drive, which is exactly the redrive-safety this unit adds.)
            await AckAsync(workItem, cancellationToken);

            await HandleHandlerCrashAsync(workItem, handlerName, faultInfo, cancellationToken);

            return new RuntimeSchedulerWorkItemResult(
                workItemId: workItem.WorkItemId,
                workflowExecutionId: workItem.WorkflowExecutionId,
                commandKind: workItem.CommandKind,
                status: RuntimeSchedulerWorkItemResultStatus.Faulted,
                handlerName: handlerName,
                startedAt: startedAt,
                completedAt: _timeProvider.GetUtcNow(),
                error: faultInfo.ToSummaryString());
        }
    }

    // A dispatched handler threw. The work item has already been ack-deleted from the durable queue by the caller (the
    // fault branch acks before invoking this method), so without this it would be dropped: no retry, no record, no
    // incident. Record it to the poison store honoring IRuntimeDomainRetryPolicy — the default (Noop → DoNotRetry) parks
    // it as Poisoned (safe, no loop). RetryNow re-enqueues immediately through the queue's public contract; RetryAfter
    // records a NextRetryAt for the durable resumption pump (RuntimeResumptionPumpTask; see
    // docs/runtime-durable-resumption.md) to re-drive and does NOT re-enqueue here, since immediate re-enqueue would
    // ignore the delay and hot-loop. Because the source item was ack-deleted first, RetryNow's re-enqueue is the *only*
    // requeue (the sweep's backlog discovery finds nothing to re-drive), so poison delivery stays bounded. This lives
    // entirely in the fault path — it does not touch the peek/pause-gate/dispatch sequence.
    private async ValueTask HandleHandlerCrashAsync(
        RuntimeSchedulerWorkItem workItem,
        string handlerName,
        RuntimeFaultInfo faultInfo,
        CancellationToken cancellationToken)
    {
        if (_poisonStore is null)
            return;

        var now = _timeProvider.GetUtcNow();
        var existing = await _poisonStore.FindAsync(workItem.WorkflowExecutionId, workItem.WorkItemId, cancellationToken);
        var priorFailureCount = existing?.FailureCount ?? 0;
        var failureCount = priorFailureCount + 1;
        var firstFailedAt = existing?.FirstFailedAt ?? now;

        var decision = _retryPolicy?.Decide(new RuntimeDomainRetryRequest(
            workflowExecutionId: workItem.WorkflowExecutionId,
            activityExecutionId: null,
            failureType: faultInfo.ExceptionType,
            failureCount: priorFailureCount,
            requestedAt: now));

        var disposition = RuntimeSchedulerPoisonDisposition.Poisoned;
        DateTimeOffset? nextRetryAt = null;

        switch (decision?.Mode)
        {
            case RuntimeDomainRetryMode.RetryNow:
                await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
                disposition = RuntimeSchedulerPoisonDisposition.RetryScheduled;
                nextRetryAt = now;
                break;
            case RuntimeDomainRetryMode.RetryAfter:
                disposition = RuntimeSchedulerPoisonDisposition.RetryScheduled;
                nextRetryAt = now + (decision.Delay ?? TimeSpan.Zero);
                break;
            case RuntimeDomainRetryMode.DoNotRetry:
            case RuntimeDomainRetryMode.Fault:
            case null:
                disposition = RuntimeSchedulerPoisonDisposition.Poisoned;
                break;
        }

        await _poisonStore.RecordAsync(new RuntimeSchedulerPoisonRecord(
            workflowExecutionId: workItem.WorkflowExecutionId,
            workItemId: workItem.WorkItemId,
            commandKind: workItem.CommandKind,
            handlerName: handlerName,
            fault: faultInfo,
            failureCount: failureCount,
            disposition: disposition,
            firstFailedAt: firstFailedAt,
            lastFailedAt: now,
            nextRetryAt: nextRetryAt,
            metadata: decision is null ? null : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime.poison.retryMode"] = decision.Mode.ToString(),
                ["runtime.poison.retryReason"] = decision.Reason
            }),
            cancellationToken);
    }

    private async ValueTask<RuntimeSchedulerWorkItem?> PeekAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var items = await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId, limit: 1), cancellationToken);
        return items.FirstOrDefault();
    }

    // Ack-deletes the dispatched item from the durable queue by consuming the head via DequeueAsync, and enforces the
    // single-writer TOCTOU tripwire (RT-2): the head consumed here MUST be the item that was just dispatched. Because
    // dispatch no longer dequeues up-front, the head cannot have advanced under a single writer between peek and ack —
    // the drain owns the execution's fencing lease for its whole duration. A mismatch (or an empty queue) means another
    // writer interleaved and drained this execution concurrently, violating the invariant that all dispatch routes
    // through the agent mailbox; fail fast. The InMemory/Groundwork queues expose no delete-by-id, so the ack is
    // expressed as "consume the FIFO head", which is the same item peek returned under single-writer ownership.
    private async ValueTask AckAsync(RuntimeSchedulerWorkItem dispatchedWorkItem, CancellationToken cancellationToken)
    {
        var acked = await _schedulerWorkQueue.DequeueAsync(dispatchedWorkItem.WorkflowExecutionId, cancellationToken);

        if (acked is null || !StringComparer.Ordinal.Equals(acked.WorkItemId, dispatchedWorkItem.WorkItemId))
            throw new InvalidOperationException(
                $"Single-writer invariant violation: scheduler drain for workflow execution '{dispatchedWorkItem.WorkflowExecutionId}' " +
                $"dispatched work item '{dispatchedWorkItem.WorkItemId}' but ack-dequeued '{acked?.WorkItemId ?? "<none>"}'. A concurrent " +
                "drainer interleaved between the pause-gate peek and the ack; all dispatch must route through the agent mailbox.");
    }

    private async ValueTask<SchedulerPauseDecision?> EvaluatePauseAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        if (_pauseGate is null)
            return null;

        return await _pauseGate.EvaluateAsync(workItem, cancellationToken);
    }

    private RuntimeSchedulerWorkItemResult CreatePausedResult(RuntimeSchedulerWorkItem workItem, SchedulerPauseDecision decision)
    {
        var now = _timeProvider.GetUtcNow();
        return new RuntimeSchedulerWorkItemResult(
            workItemId: workItem.WorkItemId,
            workflowExecutionId: workItem.WorkflowExecutionId,
            commandKind: workItem.CommandKind,
            status: RuntimeSchedulerWorkItemResultStatus.Paused,
            handlerName: nameof(WorkflowSchedulerPauseGate),
            startedAt: now,
            completedAt: now,
            error: $"Scheduler work is paused at boundary '{decision.Boundary}' by hold '{decision.HoldId}': {decision.Reason}");
    }

    private IWorkflowSchedulerWorkHandler FindHandler(RuntimeSchedulerWorkItem workItem)
    {
        foreach (var handler in _customHandlers)
        {
            if (handler.CanHandle(workItem))
                return handler;
        }

        foreach (var handler in _fallbackHandlers)
        {
            if (handler.CanHandle(workItem))
                return handler;
        }

        return new FaultingMissingSchedulerWorkHandler();
    }

    private sealed class FaultingMissingSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(FaultingMissingSchedulerWorkHandler);

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"No workflow scheduler work handler accepted command kind '{workItem.CommandKind}'.");
    }
}
