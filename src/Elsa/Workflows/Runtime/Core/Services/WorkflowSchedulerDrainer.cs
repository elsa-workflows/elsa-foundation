using Elsa.Workflows.Runtime.Core.Contracts;
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
        IRuntimeDomainRetryPolicy? retryPolicy = null)
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
    }

    public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

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

            var workItem = await _schedulerWorkQueue.DequeueAsync(request.WorkflowExecutionId, cancellationToken);
            if (workItem is null)
                break;

            // Single-writer TOCTOU tripwire (RT-2): the pause decision above was computed for the peeked head; the
            // dequeue must return that same head. A mismatch means another writer drained this execution concurrently
            // between the peek and the dequeue — a violation of the single-writer ownership invariant (all dispatch
            // MUST route through the agent mailbox). Fail fast rather than gate item B's dequeue on item A's decision.
            if (!StringComparer.Ordinal.Equals(workItem.WorkItemId, nextWorkItem.WorkItemId))
                throw new InvalidOperationException(
                    $"Single-writer invariant violation: scheduler drain for workflow execution '{request.WorkflowExecutionId}' " +
                    $"peeked work item '{nextWorkItem.WorkItemId}' but dequeued '{workItem.WorkItemId}'. A concurrent drainer " +
                    "interleaved between the pause-gate peek and the dequeue; all dispatch must route through the agent mailbox.");

            var result = await DispatchAsync(workItem, request.AmbientServices, cancellationToken);
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

        try
        {
            handler = FindHandler(workItem);

            // Move 1 (ADR 0029): route dispatch through the runtime execution pipeline when one is wired, running the
            // handler as the pipeline's inner terminal delegate. When absent, dispatch the handler directly — with only
            // the built-in pass-through middleware registered the two paths are behavior-identical. RT-7: the drain's
            // ambient services flow explicitly into the pipeline dispatcher, which stages them on the dispatch workspace
            // for slot-invoked handlers to read — no AsyncLocal service location in the drain path.
            if (_pipelineDispatcher is not null)
                await _pipelineDispatcher.DispatchAsync(workItem, handler, ambientServices, cancellationToken);
            else
                await handler.HandleAsync(workItem, cancellationToken);

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
            var handlerName = handler?.Name ?? nameof(WorkflowSchedulerDrainer);
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

    // A dispatched handler threw. The work item was already dequeued (:128), so without this it would be dropped:
    // no retry, no record, no incident. Record it to the poison store honoring IRuntimeDomainRetryPolicy — the
    // default (Noop → DoNotRetry) parks it as Poisoned (safe, no loop). RetryNow re-enqueues immediately through the
    // queue's public contract; RetryAfter records a NextRetryAt for the durable resumption pump
    // (RuntimeResumptionPumpTask; see docs/runtime-durable-resumption.md) to re-drive and does NOT
    // re-enqueue here, since immediate re-enqueue would ignore the delay and hot-loop. This lives entirely in the
    // crash path — it does not touch the peek/pause-gate/dequeue sequence.
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
