using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Exceptions;
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
    private readonly RuntimeSchedulerWorkClaimOptions _claimOptions;
    private readonly IRuntimeConsumedSchedulerWorkClaimAccessor? _consumedWorkClaimAccessor;
    private readonly RuntimeSchedulerDispatchDiagnostics? _dispatchDiagnostics;
    private readonly string _claimOwnerId = $"scheduler-drainer:{Guid.NewGuid():N}";

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
        IWorkflowEngineTracer? tracer = null,
        RuntimeSchedulerWorkClaimOptions? claimOptions = null,
        IRuntimeConsumedSchedulerWorkClaimAccessor? consumedWorkClaimAccessor = null,
        RuntimeSchedulerDispatchDiagnostics? dispatchDiagnostics = null)
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
        _faultCapturePolicy = faultCapturePolicy ?? DefaultRuntimeFaultCapturePolicy.CreateDefault();
        _poisonStore = poisonStore;
        _retryPolicy = retryPolicy;
        _tracer = tracer ?? NullWorkflowEngineTracer.Instance;
        _claimOptions = claimOptions ?? new RuntimeSchedulerWorkClaimOptions();
        _consumedWorkClaimAccessor = consumedWorkClaimAccessor;
        _dispatchDiagnostics = dispatchDiagnostics;
        if (_claimOptions.VisibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimOptions), "Scheduler work visibility timeout must be greater than zero.");
    }

    public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // MS-9: the drain-cycle span wraps the whole method. StartDrainCycle returns null when tracing is inactive, so
        // no allocation and no ambient Activity is introduced; when active, Activity.Current is set for the scope and
        // restored on dispose. This is trace context only — it is not service location and does not touch the fenced
        // claim->pause->dispatch->complete sequence below.
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

            var delivery = await AcquireNextAsync(request.WorkflowExecutionId, cancellationToken);
            if (delivery is null)
                break;
            var nextWorkItem = delivery.Item;

            var pauseDecision = await EvaluatePauseAsync(nextWorkItem, cancellationToken);
            if (pauseDecision is { CanAdvance: false })
            {
                await ReleaseAsync(delivery, cancellationToken);
                results.Add(CreatePausedResult(nextWorkItem, pauseDecision));
                break;
            }

            // Redrive-safe drain (#412 item 3 / "Window C" closure): the FIFO head is claimed, not destructively
            // dequeued, before dispatch. Claim-capable providers renew that exclusive visibility while the handler runs
            // and remove the item only after its effect is durable. A stale claimant cannot complete successor-owned
            // work. Legacy providers retain the original single-writer list/dispatch/dequeue path. In either path, a
            // crash inside the handler leaves the source item available for deterministic, idempotent redelivery.
            var result = await DispatchAsync(nextWorkItem, delivery.Claim, request.AmbientServices, cancellationToken);
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

    private async ValueTask<RuntimeSchedulerWorkItemResult> DispatchAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeSchedulerWorkClaim? claim,
        IServiceProvider? ambientServices,
        CancellationToken cancellationToken)
    {
        IWorkflowSchedulerWorkHandler? handler = null;
        var startedAt = _timeProvider.GetUtcNow();
        var renewal = claim is null ? null : new ClaimRenewalState(claim);

        // spec 123 FR-008: one dispatch per drained work item. This is the deterministic hop-count evidence for the
        // ReplaySafe fusion A/B (fusion elides the intermediate StartActivity/InvokeActivity dispatches).
        _dispatchDiagnostics?.RecordDispatch();

        // WU-1 / spec 105: stage this dispatch's claim so a checkpoint commit can fold its fence-checked deletion into the
        // commit unit-of-work. When it does, the committer marks it consumed and the separate acknowledgement below is
        // skipped. Owner+token fence is renewal-stable, so the claim captured here stays valid across renewals.
        using var consumeScope = claim is not null && _consumedWorkClaimAccessor is not null
            ? _consumedWorkClaimAccessor.Begin(ConsumedSchedulerWorkItem.FromClaim(claim))
            : null;

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
            await DispatchWithClaimRenewalAsync(
                renewal,
                dispatchCancellationToken => _pipelineDispatcher is not null
                    ? _pipelineDispatcher.DispatchAsync(workItem, handler, ambientServices, dispatchCancellationToken)
                    : handler.HandleAsync(workItem, dispatchCancellationToken),
                cancellationToken);

            // The handler's effect is now durable. On the atomic path a checkpoint commit already deleted the claimed item
            // inside its unit-of-work (WU-1 / spec 105), so the separate acknowledgement is redundant and skipped;
            // otherwise (legacy/non-atomic provider, or a dispatch that produced no commit) complete the fenced claim or
            // consume the FIFO head for a legacy single-writer provider. A crash before this point leaves the item queued
            // for idempotent re-drive.
            if (_consumedWorkClaimAccessor?.WasConsumedDurably != true)
                await AckAsync(workItem, renewal?.Current, cancellationToken);

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
        catch (RuntimeSchedulerWorkClaimLostException)
        {
            // A successor owns (or may own) this work. Never acknowledge or poison it from the stale dispatch.
            throw;
        }
        catch (RuntimeSchedulerWorkConsumeConflictException)
        {
            // The atomic commit's fence-checked consume failed: a successor reclaimed the item (WU-1 / spec 105). Treat it
            // exactly like a lost claim — the commit rolled back and persisted nothing, so never acknowledge or poison it.
            throw;
        }
        catch (Exception exception)
        {
            var faultInfo = _faultCapturePolicy.Capture(exception);
            var innerFaultInfo = _faultCapturePolicy.CaptureInner(exception);
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
            // WU-1 / spec 105: when an earlier checkpoint commit in THIS dispatch already consumed the item durably (a
            // multi-commit handler faulting after its first commit landed), the item is already gone from the queue —
            // poison delivery stays bounded without a second acknowledgement. A CompleteClaimAsync here would fail its
            // fence against the consumed claim and throw a spurious claim-lost out of this catch, skipping poison
            // recording entirely. Skip the ack but still record poison and return the Faulted result.
            if (_consumedWorkClaimAccessor?.WasConsumedDurably != true)
                await AckAsync(workItem, renewal?.Current, cancellationToken);

            await HandleHandlerCrashAsync(workItem, handlerName, faultInfo, innerFaultInfo, cancellationToken);

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
    // entirely in the fault path — it does not change claim/pause/dispatch ordering.
    private async ValueTask HandleHandlerCrashAsync(
        RuntimeSchedulerWorkItem workItem,
        string handlerName,
        RuntimeFaultInfo faultInfo,
        RuntimeFaultInfo? innerFaultInfo,
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
                [RuntimeMetadataKeys.SchedulerPoisonRetryMode] = decision.Mode.ToString(),
                [RuntimeMetadataKeys.SchedulerPoisonRetryReason] = decision.Reason
            },
            innerFault: innerFaultInfo),
            cancellationToken);
    }

    private async ValueTask<SchedulerWorkDelivery?> AcquireNextAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        if (_schedulerWorkQueue.SupportsClaimTransitions)
        {
            var claim = await _schedulerWorkQueue.ClaimAsync(
                new RuntimeSchedulerWorkClaimRequest(
                    workflowExecutionId,
                    _claimOwnerId,
                    _timeProvider.GetUtcNow(),
                    _claimOptions.VisibilityTimeout),
                cancellationToken);
            return claim is null ? null : new SchedulerWorkDelivery(claim.Item, claim);
        }

        var page = await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId, limit: 1), cancellationToken);
        var item = page.Items.FirstOrDefault();
        return item is null ? null : new SchedulerWorkDelivery(item, null);
    }

    // Completes the current fenced claim when the provider supports claim transitions. For a legacy provider, consumes
    // the FIFO head and enforces the single-writer TOCTOU tripwire (RT-2): the consumed head MUST be the item that was
    // just dispatched. A mismatch (or empty queue) means another writer interleaved; fail fast.
    private async ValueTask AckAsync(
        RuntimeSchedulerWorkItem dispatchedWorkItem,
        RuntimeSchedulerWorkClaim? claim,
        CancellationToken cancellationToken)
    {
        if (claim is not null)
        {
            var completion = await _schedulerWorkQueue.CompleteClaimAsync(claim, cancellationToken);
            if (!completion.Succeeded)
                throw NewClaimLost(claim, "complete", completion.Status);
            return;
        }

        var acked = await _schedulerWorkQueue.DequeueAsync(dispatchedWorkItem.WorkflowExecutionId, cancellationToken);

        if (acked is null || !StringComparer.Ordinal.Equals(acked.WorkItemId, dispatchedWorkItem.WorkItemId))
            throw new InvalidOperationException(
                $"Single-writer invariant violation: scheduler drain for workflow execution '{dispatchedWorkItem.WorkflowExecutionId}' " +
                $"dispatched work item '{dispatchedWorkItem.WorkItemId}' but ack-dequeued '{acked?.WorkItemId ?? "<none>"}'. A concurrent " +
                "drainer interleaved between the pause-gate peek and the ack; all dispatch must route through the agent mailbox.");
    }

    private async ValueTask ReleaseAsync(SchedulerWorkDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Claim is null)
            return;

        var release = await _schedulerWorkQueue.ReleaseClaimAsync(
            delivery.Claim,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        if (!release.Succeeded)
            throw NewClaimLost(delivery.Claim, "release", release.Status);
    }

    private async ValueTask DispatchWithClaimRenewalAsync(
        ClaimRenewalState? renewal,
        Func<CancellationToken, ValueTask> dispatch,
        CancellationToken cancellationToken)
    {
        if (renewal is null)
        {
            await dispatch(cancellationToken);
            return;
        }

        using var renewalStop = new CancellationTokenSource();
        using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewalTask = RenewClaimUntilStoppedAsync(renewal, renewalStop.Token, dispatchCancellation);
        Exception? dispatchFailure = null;
        Exception? renewalFailure = null;
        try
        {
            await dispatch(dispatchCancellation.Token);
        }
        catch (Exception exception)
        {
            dispatchFailure = exception;
        }
        finally
        {
            await renewalStop.CancelAsync();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException) when (renewalStop.IsCancellationRequested)
            {
                // Normal: dispatch completed before the next renewal cadence.
            }
            catch (Exception exception)
            {
                renewalFailure = exception;
            }
        }

        // A deadline that fires in the same instant the dispatch is completing must not poison work that already
        // succeeded: by then the handler has returned and its effects are committed, so a poison record and a blocking
        // incident would both be false. Claim-lost keeps its precedence over a successful dispatch (a successor may own
        // the item, so that dispatch's effects cannot be trusted); a deadline breach is only meaningful while the
        // dispatch is still running, which is exactly the case where dispatchFailure carries its cancellation.
        if (renewalFailure is RuntimeSchedulerDispatchDeadlineExceededException && dispatchFailure is null)
            renewalFailure = null;

        if (renewalFailure is not null)
            throw renewalFailure;
        if (dispatchFailure is not null)
            ExceptionDispatchInfo.Capture(dispatchFailure).Throw();
    }

    // Renews the dispatch's claim on a cadence until the dispatch completes — and, unlike the original loop, no longer
    // renews without limit. RuntimeSchedulerWorkClaimOptions.MaxDispatchDuration is an absolute ceiling on one
    // dispatch: past it the loop stops renewing (so the claim lapses and a survivor can take the work), cancels the
    // dispatch, and throws a deadline exception. That exception is deliberately NOT an OperationCanceledException and
    // NOT a claim-lost, so it lands in DispatchAsync's general fault arm and the item is ack-deleted, poisoned, and
    // surfaced as a blocking incident — a hang becomes a bounded, visible outcome instead of an invisible one.
    private async Task RenewClaimUntilStoppedAsync(
        ClaimRenewalState renewal,
        CancellationToken stopToken,
        CancellationTokenSource dispatchCancellation)
    {
        var cadence = TimeSpan.FromTicks(Math.Max(1, _claimOptions.VisibilityTimeout.Ticks / 3));
        var deadline = _claimOptions.MaxDispatchDuration == Timeout.InfiniteTimeSpan
            ? (DateTimeOffset?)null
            : _timeProvider.GetUtcNow() + _claimOptions.MaxDispatchDuration;
        while (true)
        {
            await Task.Delay(cadence, _timeProvider, stopToken);

            // Checked before renewing, so a breached dispatch never gets its visibility extended once more.
            if (deadline is { } limit && _timeProvider.GetUtcNow() >= limit)
            {
                await dispatchCancellation.CancelAsync();
                throw new RuntimeSchedulerDispatchDeadlineExceededException(
                    renewal.Current.Item.WorkItemId,
                    renewal.Current.Item.WorkflowExecutionId,
                    _claimOptions.MaxDispatchDuration);
            }

            // #1254: this dispatch's own checkpoint commit consumes the claim, so once that consume has landed there is
            // nothing left to renew — the item is gone by design and the exclusivity the claim bought is permanent.
            // Renewing anyway finds no document and reads as a lost claim, which would cancel and abort a dispatch whose
            // work already committed. The same check covers a consume still in flight: skipping a renewal costs at most
            // one cadence of visibility (the timeout is three cadences), and renewing into the store call that is
            // deleting the very same document is the interleaving worth avoiding.
            if (IsOwnConsumeSettlingOrSettled(renewal.Current))
                return;

            RuntimeSchedulerWorkClaimTransitionResult result;
            try
            {
                result = await _schedulerWorkQueue.RenewClaimAsync(
                    renewal.Current,
                    _timeProvider.GetUtcNow(),
                    _claimOptions.VisibilityTimeout,
                    stopToken);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (IsOwnConsumeSettlingOrSettled(renewal.Current))
                    return;
                await dispatchCancellation.CancelAsync();
                throw NewClaimLost(renewal.Current, "renew", status: null, exception);
            }

            if (result.Status != RuntimeSchedulerWorkClaimTransitionStatus.Succeeded || result.Claim is null)
            {
                // Re-checked rather than keyed on the status: our own consume surfaces as AlreadyApplied or as Stale
                // depending on where the renewal's load and save fell around the delete, and the in-memory queue reports
                // a missing item as Stale in every case. A genuine successor still lands here and still wins.
                if (IsOwnConsumeSettlingOrSettled(renewal.Current))
                    return;
                await dispatchCancellation.CancelAsync();
                throw NewClaimLost(renewal.Current, "renew", result.Status);
            }

            renewal.Current = result.Claim;
        }
    }

    // Whether this dispatch's own consume explains a claim the queue no longer honors. A landed consume is fence-checked
    // on owner+token (a successor reclaim advances the token and rolls the commit back as a consume conflict), so this
    // never trusts the effects of a dispatch a successor stole.
    private bool IsOwnConsumeSettlingOrSettled(RuntimeSchedulerWorkClaim claim) =>
        _consumedWorkClaimAccessor?.IsConsumeInFlightOrDurable(claim.Item.WorkItemId) == true;

    private static RuntimeSchedulerWorkClaimLostException NewClaimLost(
        RuntimeSchedulerWorkClaim claim,
        string transition,
        RuntimeSchedulerWorkClaimTransitionStatus? status,
        Exception? innerException = null) =>
        new(claim, transition, status, innerException);

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

    private sealed record SchedulerWorkDelivery(
        RuntimeSchedulerWorkItem Item,
        RuntimeSchedulerWorkClaim? Claim);

    private sealed class ClaimRenewalState(RuntimeSchedulerWorkClaim current)
    {
        public RuntimeSchedulerWorkClaim Current { get; set; } = current;
    }

    /// <summary>
    /// One dispatch exceeded <see cref="RuntimeSchedulerWorkClaimOptions.MaxDispatchDuration"/>. Distinct from
    /// <c>RuntimeSchedulerWorkClaimLostException</c> (a successor owns the work, so it must not be poisoned) and from
    /// <see cref="OperationCanceledException"/> (a genuine shutdown/cancel, which must leave the item queued): a
    /// deadline breach is a decided outcome about <em>this</em> item and belongs in the poison ladder.
    /// </summary>
    public sealed class RuntimeSchedulerDispatchDeadlineExceededException(
        string workItemId,
        string workflowExecutionId,
        TimeSpan maxDispatchDuration)
        : Exception(
            $"Scheduler work item '{workItemId}' in workflow execution '{workflowExecutionId}' exceeded the maximum " +
            $"dispatch duration of {maxDispatchDuration}. Renewal stopped and the dispatch was cancelled; if the " +
            "activity does not observe cancellation the claim lapses and a survivor re-drives the item.")
    {
        public string WorkItemId { get; } = workItemId;

        public string WorkflowExecutionId { get; } = workflowExecutionId;

        public TimeSpan MaxDispatchDuration { get; } = maxDispatchDuration;
    }

    private sealed class RuntimeSchedulerWorkClaimLostException(
        RuntimeSchedulerWorkClaim claim,
        string transition,
        RuntimeSchedulerWorkClaimTransitionStatus? status,
        Exception? innerException = null)
        : InvalidOperationException(
            $"Scheduler work claim for '{claim.Item.WorkItemId}' in workflow execution '{claim.Item.WorkflowExecutionId}' " +
            $"was lost during '{transition}'" +
            (status is null ? "." : $" with status '{status}'."),
            innerException);
}
