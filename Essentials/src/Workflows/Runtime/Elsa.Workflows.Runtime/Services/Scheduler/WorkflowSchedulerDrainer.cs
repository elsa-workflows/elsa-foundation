using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Workflows.Runtime.Services.Scheduler;

/// <summary>
/// Claims scheduler work for one workflow execution and dispatches it, one item at a time. The loop has four exits:
/// the queue runs dry, an item faults, an item pauses, or the per-drain work-item budget
/// (<see cref="RuntimeSchedulerDrainRequest.MaxWorkItems"/>, unbounded by default) is exhausted. The terminal-status
/// gate stops it as well, checked on entry and after each dispatched item.
///
/// <para>Only the first three are reported. This method names no stop reason of its own except
/// <see cref="RuntimeSchedulerDrainStopReason.WorkflowTerminated"/>, so a budget-truncated drain is inferred as
/// <see cref="RuntimeSchedulerDrainStopReason.Quiesced"/> and is indistinguishable downstream from a settled one,
/// even though work is still queued.</para>
///
/// <para><b>This loop does not decide what a fault does to a workflow.</b> Every collaborator that decides an outcome
/// sits behind an interface this class does not name, so reading it end to end will not tell you what happens when
/// something throws. Two paths exist and they are not the same path:</para>
/// <list type="bullet">
/// <item><description>An <b>activity</b> fault never reaches <see cref="DispatchAsync"/>'s fault arm at all. The
/// invoking work handler catches it and records a blocking incident, and the dispatch reports
/// <see cref="RuntimeSchedulerWorkItemResultStatus.Completed"/>. Whether that incident terminates the workflow is
/// decided later by the workflow's authored <c>IIncidentStrategy</c>, applied by
/// <c>IncidentStrategyResolutionDrainObserver</c> at quiescence, with
/// <c>BlockingIncidentWorkflowFaultObserver</c> as the backstop for drains that did not quiesce.</description></item>
/// <item><description>A <b>handler</b> fault lands in <see cref="DispatchAsync"/>'s general catch. A drain that is
/// found there to have lost its run's ownership lease is turned away and writes nothing; one found to have lost it
/// only when the poison record is written has made its queue change and records nothing (#4032). Otherwise the fault
/// is poisoned by <see cref="HandleHandlerCrashAsync"/>, and is projected into a blocking incident by
/// <c>PoisonedSchedulerWorkIncidentObserver</c>, which deliberately leaves the workflow non-terminal. A handler fault a
/// checkpoint rule caused is marked <see cref="RuntimeSchedulerWorkItemResult.CheckpointRuleViolation"/>, and the drain
/// orchestrator faults the workflow for it (#1780).</description></item>
/// </list>
///
/// <para>Both paths, the defaults that decide them, and the source for each claim are mapped in
/// <c>docs/runtime-fault-behavior.md</c>. Read that before concluding anything about fault behavior from this file.</para>
/// </summary>
public sealed class WorkflowSchedulerDrainer : IWorkflowSchedulerDrainer
{
    internal DrainControlContext? DrainControls { private get; init; }
    internal RuntimeExecutionProtocolSelector? ModelProtocols { private get; init; }

    /// <summary>
    /// Whether something re-drives work released with a future visibility: an <c>IWorkflowDispatchDurabilityEvidence</c>
    /// naming <c>Resumption</c>, the same signal the router uses before parking shed work (#1320). Without it a
    /// <c>RetryAfter</c> is poisoned rather than released to wait for a sweep nobody runs.
    /// </summary>
    internal bool HasResumptionRedriver { private get; init; }

    /// <summary>
    /// The accessor for the ambient ownership lease (#4032). Optional; the runtime composition root sets it. The
    /// drainer reads it only when a work handler faults, and uses the lease only when it is a lease of the work
    /// item's own execution. Under such a lease it asks <see cref="OwnershipService"/>, when that is set, before it
    /// writes anything of the fault, and it writes the poison record under the lease when the poison store can check
    /// it atomically (<see cref="ISchedulerPoisonFenceCapability"/>). Without the accessor neither happens, and the
    /// record is written with no ownership check.
    /// </summary>
    public IRuntimeExecutionOwnershipContextAccessor? OwnershipContextAccessor { private get; init; }

    /// <summary>
    /// The ownership service the drainer asks, when a work handler faults under an ambient lease for the work item's
    /// own execution, whether that lease is still the run's current one (#4032). Optional; the runtime composition
    /// root sets it. It is asked before the drainer acknowledges, releases or re-enqueues the faulted step. A lease
    /// that another owner took over, that expired, or that an alteration borrowed ends the drain with
    /// <see cref="RuntimeStaleFencingTokenException"/>: the fault arm writes nothing, so the step is left as it was
    /// and nothing is recorded. Only that answer stops the fault path. When the service cannot answer, because its
    /// read failed, the fault path goes on as it does without the service: the step is acknowledged, and the record
    /// is still written under the lease where the poison store can check it. Without the service the drainer does
    /// not ask, and only a fenced poison record checks the lease. It is not used without
    /// <see cref="OwnershipContextAccessor"/>.
    /// </summary>
    public IRuntimeExecutionOwnershipService? OwnershipService { private get; init; }
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IReadOnlyCollection<IWorkflowSchedulerWorkHandler> _customHandlers;
    private readonly IReadOnlyCollection<IWorkflowSchedulerWorkHandler> _fallbackHandlers;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowSchedulerPauseGate _pauseGate;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IRuntimeExecutionPipelineDispatcher? _pipelineDispatcher;
    private readonly IRuntimeFaultCapturePolicy _faultCapturePolicy;
    private readonly IWorkflowSchedulerPoisonStore _poisonStore;
    private readonly IRuntimeDomainRetryPolicy? _retryPolicy;
    private readonly IWorkflowEngineTracer _tracer;
    private readonly RuntimeSchedulerWorkClaimOptions _claimOptions;
    private readonly IRuntimeConsumedSchedulerWorkClaimAccessor? _consumedWorkClaimAccessor;
    private readonly RuntimeSchedulerDispatchDiagnostics? _dispatchDiagnostics;
    private readonly IRuntimeAdmissionLoadSignal? _admissionLoadSignal;
    private readonly string _claimOwnerId = $"scheduler-drainer:{Guid.NewGuid():N}";

    /// <summary>
    /// Creates the drainer. The seven telescoping constructors collapsed into this single primary constructor —
    /// five required collaborators (<paramref name="schedulerWorkQueue"/>, <paramref name="handlers"/>,
    /// <paramref name="workflowExecutionStateStore"/>, <paramref name="poisonStore"/>, <paramref name="pauseGate"/>)
    /// followed by optional collaborators that default to their no-op/system implementations. The workflow execution
    /// state store is <b>required by construction</b> so the single-writer terminal-status guard (which stops sibling work once an
    /// execution reaches a terminal status) can never be silently disabled by picking a narrower constructor.
    ///
    /// <para>The poison store is <b>required by construction</b> for the same reason (#1271): a handler fault is
    /// ack-deleted from the durable queue before poison handling runs, so a drainer built without a poison store loses
    /// the item entirely — nothing recorded, no incident projected, and <see cref="IRuntimeDomainRetryPolicy"/> never
    /// consulted, so even a <c>RetryNow</c> decision does not re-enqueue. Requiring the store makes that configuration
    /// impossible to construct rather than merely improbable; the core composition root already registers an
    /// <c>InMemoryWorkflowSchedulerPoisonStore</c> unconditionally, so it costs nothing at composition time.</para>
    ///
    /// <para>The pause gate is <b>required by construction</b> for a third reason of the same kind (#1277 R1): its
    /// absence does not degrade the drain, it defeats the pause contract outright. A null gate made the drain loop's
    /// pause evaluation answer null, which fails its <c>{ CanAdvance: false }</c> pattern, so a workflow under an
    /// effective hold had its queued work claimed and dispatched anyway. An empty hold
    /// store is the way to express "nothing is held" — the gate then answers <c>CanAdvance: true</c> in band — so
    /// there is no configuration that needs the gate itself to be absent.</para>
    ///
    /// <para><paramref name="retryPolicy"/> stays optional because its absence is indistinguishable from the registered
    /// default answering <c>DoNotRetry</c>: both park the item as <c>Poisoned</c>, and the record is written either
    /// way.</para>
    /// </summary>
    public WorkflowSchedulerDrainer(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IEnumerable<IWorkflowSchedulerWorkHandler> handlers,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowSchedulerPoisonStore poisonStore,
        IWorkflowSchedulerPauseGate pauseGate,
        TimeProvider? timeProvider = null,
        IRuntimeExecutionPipelineDispatcher? pipelineDispatcher = null,
        IRuntimeFaultCapturePolicy? faultCapturePolicy = null,
        IRuntimeDomainRetryPolicy? retryPolicy = null,
        IWorkflowEngineTracer? tracer = null,
        RuntimeSchedulerWorkClaimOptions? claimOptions = null,
        IRuntimeConsumedSchedulerWorkClaimAccessor? consumedWorkClaimAccessor = null,
        RuntimeSchedulerDispatchDiagnostics? dispatchDiagnostics = null,
        IRuntimeAdmissionLoadSignal? admissionLoadSignal = null)
    {
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(poisonStore);
        ArgumentNullException.ThrowIfNull(pauseGate);

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
        _admissionLoadSignal = admissionLoadSignal;
        if (_claimOptions.VisibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimOptions), "Scheduler work visibility timeout must be greater than zero.");
        if (_claimOptions.UnavailableRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(claimOptions), "Scheduler work unavailable retry delay must be greater than zero.");
    }

    public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The drain-cycle span wraps the whole method. StartDrainCycle returns null when tracing is inactive, so
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

        var yielded = false;
        while (remaining > 0 && !stoppedOnTerminalStatus)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DrainControls?.ShouldYield(request.WorkflowExecutionId) == true)
            {
                yielded = true;
                break;
            }
            var delivery = await AcquireNextAsync(request.WorkflowExecutionId, cancellationToken);
            if (delivery is null)
                break;
            var nextWorkItem = delivery.Item;

            // A null decision here means this command kind has no pause boundary (the gate maps only StartActivity,
            // InvokeActivity, and GeneratedEvent), not that pausing is unavailable — the gate is required by
            // construction, so "no gate" is not a state this can be in. (#1277 R1)
            var pauseDecision = ModelProtocols?.IsSelectedModel(request.WorkflowExecutionId) == true
                ? null : await _pauseGate.EvaluateAsync(nextWorkItem, cancellationToken);
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

            if (result.Status is RuntimeSchedulerWorkItemResultStatus.Faulted or RuntimeSchedulerWorkItemResultStatus.Deferred)
                break;

            // The dispatched item may have committed a terminal status (e.g. the Finish path). Re-check so the
            // remaining queued siblings are not dequeued on the next iteration.
            stoppedOnTerminalStatus = await IsWorkflowTerminatedAsync(request.WorkflowExecutionId, cancellationToken);
        }

        // The loop only continues past a Completed result, so a terminal stop always coincides with an
        // all-completed drain; flag it as the stop reason.
        var stopReason = stoppedOnTerminalStatus
            ? RuntimeSchedulerDrainStopReason.WorkflowTerminated
            : yielded ? RuntimeSchedulerDrainStopReason.Yielded : (RuntimeSchedulerDrainStopReason?)null;

        // Outcome tags set after the loop, from already-computed values (no pre-computation, no extra work).
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
        if (ModelProtocols?.IsSelectedModel(workflowExecutionId) == true) return false;
        // The terminal-status guard reads the state store injected by construction — no AsyncLocal
        // service-location in the drain path. The store is required, so there is no null fallback.
        var state = await _workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        return state is not null && state.Status.IsTerminal();
    }

    /// <summary>
    /// Dispatches one claimed work item and classifies its outcome. The fault arm below handles a <b>handler</b>
    /// fault only. An activity that throws is caught inside the invoking handler and recorded as a blocking incident,
    /// so it arrives here as a completed dispatch; see <c>docs/runtime-fault-behavior.md</c> for the two paths and
    /// which collaborator decides each one.
    /// </summary>
    private async ValueTask<RuntimeSchedulerWorkItemResult> DispatchAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeSchedulerWorkClaim? claim,
        IServiceProvider? ambientServices,
        CancellationToken cancellationToken)
    {
        IWorkflowSchedulerWorkHandler? handler = null;
        var startedAt = _timeProvider.GetUtcNow();
        var renewal = claim is null ? null : new ClaimRenewalState(claim);

        // One dispatch per drained work item. This is the deterministic hop-count evidence for the
        // ReplaySafe fusion A/B (fusion elides the intermediate StartActivity/InvokeActivity dispatches).
        _dispatchDiagnostics?.RecordDispatch();

        // RB1 (#1235): the same event charged to the admission load reading, which is what makes the admission limit
        // a limit on dispatches rather than on runs. An External-leaf run pays ~56 of these where a fusable run pays
        // ~5, so charging per dispatch is the difference between a limit that means the same work for both shapes and
        // one that admits an order of magnitude more for the shape production traffic has.
        _admissionLoadSignal?.RecordDispatch();

        // Stage this dispatch's claim so a checkpoint commit can fold its fence-checked deletion into the
        // commit unit-of-work. When it does, the committer marks it consumed and the separate acknowledgement below is
        // skipped. Owner+token fence is renewal-stable, so the claim captured here stays valid across renewals.
        using var consumeScope = claim is not null && _consumedWorkClaimAccessor is not null
            ? _consumedWorkClaimAccessor.Begin(ConsumedSchedulerWorkItem.FromClaim(claim))
            : null;

        // #3275, #3683: the positive evidence a delayed release needs, that this dispatch sent no non-retaining checkpoint
        // to the store, for a RetryAfter and for temporarily unavailable work alike. Open only where such a release is
        // possible: a claimed item of a kind that supports it.
        using var dispatchFrame = claim is not null && RuntimeUnavailableWork.SupportsDelayedRelease(workItem.CommandKind)
            ? RuntimeSchedulerDispatchFrame.Open(workItem.WorkflowExecutionId)
            : null;
        var handlerReturned = false;

        // The dispatch span nests under the drain-cycle span via Activity.Current. The activity-execution span
        // (Invoke slot) and the checkpoint-commit span both nest under this one when the pipeline runs. Null when
        // tracing is inactive.
        using var activity = _tracer.StartDispatch(workItem);

        try
        {
            handler = FindHandler(workItem);
            activity?.SetTag(WorkflowEngineTelemetry.HandlerNameTag, handler.Name);

            // Route dispatch through the runtime execution pipeline when one is wired, running the
            // handler as the pipeline's inner terminal delegate. When absent, dispatch the handler directly — with only
            // the built-in pass-through middleware registered the two paths are behavior-identical. The drain's
            // ambient services flow explicitly into the pipeline dispatcher, which stages them on the dispatch workspace
            // for slot-invoked handlers to read — no AsyncLocal service location in the drain path.
            await DispatchWithClaimRenewalAsync(
                renewal,
                dispatchCancellationToken => _pipelineDispatcher is not null && ModelProtocols?.IsSelectedModel(workItem.WorkflowExecutionId) != true
                    ? _pipelineDispatcher.DispatchAsync(workItem, handler, ambientServices, dispatchCancellationToken)
                    : handler.HandleAsync(workItem, dispatchCancellationToken),
                cancellationToken);
            handlerReturned = true;

            // The handler's effect is now durable. On the atomic path a checkpoint commit already deleted the claimed item
            // inside its unit-of-work, so the separate acknowledgement is redundant and skipped;
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
            // The atomic commit's fence-checked consume failed: a successor reclaimed the item. Treat it
            // exactly like a lost claim — the commit rolled back and persisted nothing, so never acknowledge or poison it.
            throw;
        }
        catch (RuntimeStaleFencingTokenException)
        {
            // A commit was refused because another node now owns the run. The work is the new owner's to run, so this
            // node neither acknowledges it nor records it as poison, which would leave the new owner nothing to run.
            throw;
        }
        catch (RuntimeSchedulerWorkUnavailableException exception) when (
            dispatchFrame is not { NonRetainingCheckpointAttempted: true } || _consumedWorkClaimAccessor?.WasConsumedDurably == true)
        {
            // #3683: only work that sent no non-retaining checkpoint to the store is deferred here. One that did has an
            // effect that is durable, or will be at the coalescing segment's flush, without its commit having consumed the
            // item (an active segment consumes nothing, and a commit through a scope of its own holds no claim), so a
            // release would run it again over that effect. It falls through to the handler-fault arm below instead. A
            // commit the store refused with this same exception, rolled back, is no attempt (the committer withdraws it),
            // so a refused native worker admission is still deferred and retried here.

            // Supported work preserves its retry delay through a durable claim or a visibility-aware overlay
            // whose drain flush persists the release. Unsupported work/provider pairs retain their source.
            if (!CanReleaseWithDelay(workItem, renewal))
                throw;

            var activeClaim = renewal!;
            var visibleAt = _timeProvider.GetUtcNow().Add(_claimOptions.UnavailableRetryDelay);
            var release = await _schedulerWorkQueue.ReleaseClaimAsync(activeClaim.Current, visibleAt, cancellationToken);
            if (!release.Succeeded)
                throw NewClaimLost(activeClaim.Current, "release after temporary unavailability", release.Status);

            if (activity is not null)
                activity.SetTag(WorkflowEngineTelemetry.OutcomeTag, "deferred");

            return new RuntimeSchedulerWorkItemResult(
                workItemId: workItem.WorkItemId,
                workflowExecutionId: workItem.WorkflowExecutionId,
                commandKind: workItem.CommandKind,
                status: RuntimeSchedulerWorkItemResultStatus.Deferred,
                handlerName: handler?.Name ?? nameof(WorkflowSchedulerDrainer),
                startedAt: startedAt,
                completedAt: _timeProvider.GetUtcNow(),
                error: exception.SafeSummary);
        }
        catch (Exception) when (ModelProtocols?.IsSelectedModel(workItem.WorkflowExecutionId) == true)
        {
            // A model driver failure leaves its accepted input owed. Native poison/incident policy cannot decide
            // the outcome of a different semantic engine or acknowledge an unavailable decision.
            throw;
        }
        catch (Exception exception)
        {
            // #4032: before this arm writes anything, find out whether the drain still owns the run. A drain whose
            // lease another owner took over, that expired, or that an alteration borrowed has no say over the step:
            // its acknowledgement would remove a step the owner has to run, and its poison record would become a
            // blocking incident on a run the owner runs correctly. A refusal leaves the drain as
            // RuntimeStaleFencingTokenException, as a refused commit does above: this arm acknowledges, releases,
            // re-enqueues and records nothing. The step stays queued, unless a checkpoint commit earlier in this
            // dispatch already consumed its durable copy: outside a coalescing segment through the commit's own
            // consume (WasConsumedDurably), inside one through a boundary or cap-fold flush that advanced the durable
            // queue. Then the item is gone and what the owner has is that commit. The check comes first, so a
            // refusal costs one ownership read and no poison-store read or retry decision. Whether the poison store
            // can record under the lease is settled here as well, before the acknowledgement. Without an ambient
            // lease for this execution there is no check and no fence.
            var poisonFence = await EnsureOwnedAndResolvePoisonFenceAsync(workItem, activity, cancellationToken);
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

            // Ack-delete before poison handling (#412 item 3): a *handler fault* is a decided outcome, not a crash — the
            // drainer stays alive and records the item to the poison store. Three reads come first: the ownership check
            // and the poison store's fence capability, both above, and then the retry decision. Because the fault is a
            // decided outcome, the source item must be removed from the durable queue here, before
            // HandleHandlerCrashAsync records poison / honors a RetryNow re-enqueue;
            // otherwise the still-queued source item would be re-driven by the sweep AND re-enqueued by RetryNow, and a
            // deterministically-poisoning handler would hot-loop (redeliver forever). Ack-on-fault makes poison delivery
            // bounded. (A process crash — as opposed to a handler fault — never reaches this line, so the item stays
            // queued for idempotent re-drive, which is exactly the redrive-safety this unit adds.)
            // When an earlier checkpoint commit in THIS dispatch already consumed the item durably (a
            // multi-commit handler faulting after its first commit landed), the item is already gone from the queue —
            // poison delivery stays bounded without a second acknowledgement. A CompleteClaimAsync here would fail its
            // fence against the consumed claim and throw a spurious claim-lost out of this catch, skipping poison
            // recording entirely. Skip the ack but still record poison and return the Faulted result.
            //
            // RetryAfter (#3275) is the exception: where the claim can carry a delay and a resumption sweep is
            // composed to re-drive it, the item is released with the policy's delay instead, exactly like temporarily
            // unavailable work. It stays the only copy, becomes claimable when due, and the sweep's backlog discovery
            // re-drives it. The release needs positive evidence that running the item again repeats no checkpoint: the
            // handler itself threw, rather than the acknowledgement after it returned, and the dispatch frame saw no
            // non-retaining checkpoint go to the store. A checkpoint that went may be durable whether or not the call
            // returned, a coalescing segment may hold it for its flush, and a handler may commit through a scope of
            // its own, so none of that is inferred after the fact. A release the queue refuses falls back to the
            // acknowledgement; everything not released is poisoned. A claim that was lost (an inactive coalescing
            // segment whose durable copy is gone or held under another owner's live claim, say) refuses that
            // acknowledgement too, so the drain throws claim-lost and records nothing. A delay that ends past the last
            // representable time is poisoned as well: no queue can persist it, and releasing it anyway would park the
            // step for good.
            //
            // Temporary unavailability lands here too once the dispatch has sent a non-retaining checkpoint to the store
            // that the store did not refuse as unavailable (#3683). It cannot be deferred without repeating that
            // checkpoint, and acknowledging it with no record would silently drop a step that did not finish. In an
            // active coalescing segment the acknowledgement goes through the overlay, so the drain's flush still commits
            // the effect and removes the durable copy.
            var retry = await DecideRetryAsync(workItem, faultInfo, cancellationToken);
            DateTimeOffset? releasedUntil = null;
            if (retry.Decision is { Mode: RuntimeDomainRetryMode.RetryAfter, Delay: { } delay } && !handlerReturned && HasResumptionRedriver &&
                dispatchFrame is { NonRetainingCheckpointAttempted: false } && delay <= DateTimeOffset.MaxValue - retry.Now &&
                !RuntimeCheckpointCommitValidationException.IsCauseOf(exception) && CanReleaseWithDelay(workItem, renewal))
            {
                var visibleAt = retry.Now + delay;
                if ((await _schedulerWorkQueue.ReleaseClaimAsync(renewal!.Current, visibleAt, cancellationToken)).Succeeded)
                    releasedUntil = visibleAt;
            }
            if (releasedUntil is null && _consumedWorkClaimAccessor?.WasConsumedDurably != true)
                await AckAsync(workItem, renewal?.Current, cancellationToken);

            await HandleHandlerCrashAsync(workItem, handlerName, exception, faultInfo, innerFaultInfo, retry, releasedUntil, poisonFence, activity, cancellationToken);

            return new RuntimeSchedulerWorkItemResult(
                workItemId: workItem.WorkItemId,
                workflowExecutionId: workItem.WorkflowExecutionId,
                commandKind: workItem.CommandKind,
                status: RuntimeSchedulerWorkItemResultStatus.Faulted,
                handlerName: handlerName,
                startedAt: startedAt,
                completedAt: _timeProvider.GetUtcNow(),
                error: faultInfo.ToSummaryString(),
                checkpointRuleViolation: RuntimeCheckpointCommitValidationException.IsCauseOf(exception));
        }
    }

    // Only a lease of the item's own execution counts, as in RuntimeCheckpointCommitter.AttachExpectedFence. The
    // ownership service is optional: without it the lease is not checked here, and a fenced record still checks it.
    //
    // Only a confirmed lost lease stops the fault path. The ownership read is a provider read and can fail, and so
    // can the poison store's answer to whether it can record under the lease. Neither failure says the run was lost.
    // Stopping on one would throw out of a drain that owns its run, and inside an active coalescing segment the
    // orchestrator does not flush after a drain that threw, so the segment's buffered checkpoints would go to replay.
    // So the fault path goes on, and the dispatch span is tagged. A failed ownership read does not switch the fence
    // off: the record is still written under the lease where the store can check it, which turns a lost lease away
    // there. A store that cannot say whether it can check has its record written with no ownership check.
    private async ValueTask<PoisonFence?> EnsureOwnedAndResolvePoisonFenceAsync(
        RuntimeSchedulerWorkItem workItem,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        if (OwnershipContextAccessor?.Current is not { } lease ||
            !StringComparer.Ordinal.Equals(lease.WorkflowExecutionId, workItem.WorkflowExecutionId))
            return null;

        if (OwnershipService is { } ownershipService)
        {
            try
            {
                await ownershipService.EnsureCurrentAsync(workItem.WorkflowExecutionId, lease.FencingToken, cancellationToken);
            }
            catch (Exception failure) when (SaysNothingAboutOwnership(failure, cancellationToken))
            {
                TagFenceNotApplied(activity, failure);
            }
        }

        try
        {
            return _poisonStore is ISchedulerPoisonFenceCapability { CanRecordFenced: true } store
                ? new PoisonFence(store, lease.ToFence())
                : null;
        }
        catch (Exception failure) when (SaysNothingAboutOwnership(failure, cancellationToken))
        {
            TagFenceNotApplied(activity, failure);
            return null;
        }
    }

    // Two exceptions do say something, and are let through: the stale fence, which is the lost lease, and the
    // cancellation of this drain. Only an OperationCanceledException raised while the drain's own token is cancelled
    // is that cancellation. A store whose client reports a timeout as one has failed like any other store.
    private static bool SaysNothingAboutOwnership(Exception failure, CancellationToken cancellationToken) =>
        failure is not RuntimeStaleFencingTokenException &&
        !(failure is OperationCanceledException && cancellationToken.IsCancellationRequested);

    // The innermost exception names the cause: a store that wraps provider failures would otherwise put its own
    // exception type on every span.
    private static void TagFenceNotApplied(Activity? activity, Exception failure)
    {
        if (activity is null)
            return;

        var cause = failure.GetBaseException().GetType();
        activity.SetTag(WorkflowEngineTelemetry.PoisonFenceFallbackTag, cause.FullName ?? cause.Name);
    }

    private readonly record struct PoisonFence(ISchedulerPoisonFenceCapability Store, RuntimeExecutionFence Fence);

    private async ValueTask<RetryDecisionContext> DecideRetryAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeFaultInfo faultInfo,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var existing = await _poisonStore.FindAsync(workItem.WorkflowExecutionId, workItem.WorkItemId, cancellationToken);
        RuntimeDomainRetryDecision? decision;
        string? policyFailure = null;
        try
        {
            decision = _retryPolicy?.Decide(new RuntimeDomainRetryRequest(
                workflowExecutionId: workItem.WorkflowExecutionId,
                activityExecutionId: null,
                failureType: faultInfo.ExceptionType,
                failureCount: existing?.FailureCount ?? 0,
                requestedAt: now));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A policy that cannot decide must not leave the item claimed to loop on every lapse: it is poisoned, and
            // so surfaces as an incident. The record names the policy failure so it is not mistaken for DoNotRetry.
            decision = null;
            policyFailure = exception.GetType().FullName ?? exception.GetType().Name;
        }
        return new(now, existing, decision, policyFailure);
    }

    private readonly record struct RetryDecisionContext(
        DateTimeOffset Now,
        RuntimeSchedulerPoisonRecord? Existing,
        RuntimeDomainRetryDecision? Decision,
        string? PolicyFailure);

    // One delayed-release rule for temporarily unavailable work and for an honored RetryAfter.
    private bool CanReleaseWithDelay(RuntimeSchedulerWorkItem workItem, ClaimRenewalState? renewal) =>
        RuntimeUnavailableWork.SupportsDelayedRelease(workItem.CommandKind) &&
        renewal is not null && _consumedWorkClaimAccessor?.WasConsumedDurably != true &&
        (_schedulerWorkQueue is not IRuntimeSchedulerWorkDeferralCapability capability || capability.CanDefer(renewal.Current));

    // A dispatched handler threw. The caller has already removed the source item from the durable queue, or, for an
    // honored RetryAfter, released it with the policy's delay, so without this record a removed item would be dropped:
    // no retry, no record, no incident. The default policy (Noop → DoNotRetry) parks it as Poisoned (safe, no loop).
    // RetryNow re-enqueues immediately through the queue's public contract; because the source item was ack-deleted
    // first, that is the *only* requeue, so poison delivery stays bounded. RetryAfter is RetryScheduled only when the
    // caller released the item (releasedUntil): the queue then holds its single copy until NextRetryAt, and the
    // resumption sweep re-drives it once claimable. A RetryAfter that was not released (no resumption sweep composed, a
    // kind without delayed release, a legacy provider, a fault after the handler returned, a dispatch that sent a
    // non-retaining checkpoint to the store, a refused release, a delay past the last representable time, or a
    // checkpoint-rule fault) is Poisoned, so it surfaces as a blocking incident instead of waiting on a re-drive nothing
    // performs (#3275). A policy that throws counts as no decision. This lives entirely in the fault path — it does not
    // change claim/pause/dispatch ordering.
    //
    // What the operator eventually sees is decided elsewhere: PoisonedSchedulerWorkIncidentObserver projects a parked
    // Poisoned record into a blocking incident after the drain, and deliberately leaves the workflow non-terminal.
    // docs/runtime-fault-behavior.md maps that hand-off and contrasts it with the activity-fault path.
    //
    // #1271: this method has no "no store, nothing to do" exit. There cannot be one — unless it released a scheduled
    // retry, the caller has already ack-deleted the item, so returning early would drop it silently. The store is
    // required by construction instead.
    private async ValueTask HandleHandlerCrashAsync(
        RuntimeSchedulerWorkItem workItem,
        string handlerName,
        Exception exception,
        RuntimeFaultInfo faultInfo,
        RuntimeFaultInfo? innerFaultInfo,
        RetryDecisionContext retry,
        DateTimeOffset? releasedUntil,
        PoisonFence? poisonFence,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var (now, existing, decision, policyFailure) = retry;
        var failureCount = (existing?.FailureCount ?? 0) + 1;
        var firstFailedAt = existing?.FirstFailedAt ?? now;

        var disposition = RuntimeSchedulerPoisonDisposition.Poisoned;
        DateTimeOffset? nextRetryAt = null;

        switch (decision?.Mode)
        {
            case RuntimeDomainRetryMode.RetryNow:
                await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
                disposition = RuntimeSchedulerPoisonDisposition.RetryScheduled;
                nextRetryAt = now;
                break;
            case RuntimeDomainRetryMode.RetryAfter when releasedUntil is { } retryAt:
                disposition = RuntimeSchedulerPoisonDisposition.RetryScheduled;
                nextRetryAt = retryAt;
                break;
            case RuntimeDomainRetryMode.RetryAfter:
            case RuntimeDomainRetryMode.DoNotRetry:
            case RuntimeDomainRetryMode.Fault:
            case null:
                disposition = RuntimeSchedulerPoisonDisposition.Poisoned;
                break;
        }

        var metadata = BuildPoisonMetadata(workItem, existing, decision, exception);
        if (policyFailure is not null)
        {
            metadata[RuntimeMetadataKeys.SchedulerPoisonRetryMode] = "PolicyFailed";
            metadata[RuntimeMetadataKeys.SchedulerPoisonRetryReason] = policyFailure;
        }
        var record = new RuntimeSchedulerPoisonRecord(
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
            metadata: metadata,
            innerFault: innerFaultInfo);

        // #4032: a caller that has the ownership service checked the run's ownership before it wrote anything of this
        // fault. Where the store can check the lease in the same unit of work, the record is written under it, which
        // covers the time since that check. Only a lost lease refuses the record: RuntimeStaleFencingTokenException
        // leaves the drain and nothing is recorded. The step's acknowledgement, its release or its RetryNow
        // re-enqueue is not undone, so that refusal leaves what a process crash at this point leaves: the queue
        // change without a record. In an active coalescing segment a re-enqueue, and the acknowledgement or release
        // of a step claimed from the segment's overlay, are only in that overlay, and the orchestrator does not flush
        // after a drain that threw, so nothing this arm writes is durable there.
        //
        // A fenced write that fails for any other reason says nothing about ownership: a write conflict the store
        // gave up on, a transaction it could not open, a capability that changed its answer. The drain owned the
        // run when it was checked, so the record then goes the way it goes for a store without the capability. An
        // owning drain therefore does not end with its step acknowledged and no record because the fenced write
        // failed that way. It still can, as before, when its own token is cancelled here or when the unfenced
        // RecordAsync fails as well. The dispatch span is tagged with the type of the failure's cause, so a host
        // whose fenced write keeps failing can see that its records are unfenced. The cancellation of this drain is
        // let through as well; an OperationCanceledException raised while the drain's token is not cancelled is a
        // failed write like any other. With no lease for this execution, or a store that cannot check it, the
        // record is written unfenced.
        if (poisonFence is { } fenced)
        {
            try
            {
                await fenced.Store.RecordFencedAsync(record, fenced.Fence, cancellationToken);
                return;
            }
            catch (Exception fencedFailure) when (SaysNothingAboutOwnership(fencedFailure, cancellationToken))
            {
                TagFenceNotApplied(activity, fencedFailure);
            }
        }

        await _poisonStore.RecordAsync(record, cancellationToken);
    }

    private static Dictionary<string, string> BuildPoisonMetadata(
        RuntimeSchedulerWorkItem workItem,
        RuntimeSchedulerPoisonRecord? existing,
        RuntimeDomainRetryDecision? retryDecision,
        Exception exception)
    {
        var metadata = existing?.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                       ?? new Dictionary<string, string>(StringComparer.Ordinal);

        // The same work item may be retried after an expression failure and later fail for an unrelated reason.
        // Refresh address fields from its immutable payload and clear only the typed evidence for the previous failure.
        metadata.Remove(RuntimeMetadataKeys.InputFailureCode);
        metadata.Remove(RuntimeMetadataKeys.InputKey);
        metadata.Remove(RuntimeMetadataKeys.ExpressionLanguage);
        metadata.Remove(RuntimeMetadataKeys.InputEvaluationPhase);
        if (HasActivityAddressPayload(workItem.CommandKind) && workItem.Payload is { } payload)
        {
            AddPayloadString(payload, "activityExecutionId", RuntimeMetadataKeys.ActivityExecutionId, metadata);
            AddPayloadString(payload, "executableNodeId", RuntimeMetadataKeys.ExecutableNodeId, metadata);
        }

        if (exception is ExpressionInputFailureException inputFailure)
        {
            metadata[RuntimeMetadataKeys.InputFailureCode] = inputFailure.InputFailureCode;
            metadata[RuntimeMetadataKeys.InputKey] = inputFailure.InputKey;
            metadata[RuntimeMetadataKeys.ExpressionLanguage] = inputFailure.ExpressionLanguage;
            metadata[RuntimeMetadataKeys.InputEvaluationPhase] = inputFailure.EvaluationPhase;
        }

        // A later retry-policy decision replaces its two fields, while older records without a new decision retain
        // the last recorded retry context alongside the stable activity address.
        if (retryDecision is not null)
        {
            metadata[RuntimeMetadataKeys.SchedulerPoisonRetryMode] = retryDecision.Mode.ToString();
            metadata[RuntimeMetadataKeys.SchedulerPoisonRetryReason] = retryDecision.Reason;
        }

        return metadata;
    }

    private static bool HasActivityAddressPayload(WorkflowExecutionCommandKind commandKind) =>
        commandKind is WorkflowExecutionCommandKind.ScheduleActivity
            or WorkflowExecutionCommandKind.CompleteActivity
            or WorkflowExecutionCommandKind.ResumeBookmark
            or WorkflowExecutionCommandKind.CreateBookmark
            or WorkflowExecutionCommandKind.StartActivity
            or WorkflowExecutionCommandKind.InvokeActivity
            or WorkflowExecutionCommandKind.NotifyParentActivity
            or WorkflowExecutionCommandKind.DeliverChildControl
            or WorkflowExecutionCommandKind.DeliverInterventionDecision
            or WorkflowExecutionCommandKind.RetryActivityBoundary
            or WorkflowExecutionCommandKind.CancelActivityScope;

    private static void AddPayloadString(
        System.Text.Json.JsonElement payload,
        string propertyName,
        string metadataKey,
        IDictionary<string, string> metadata)
    {
        if (payload.ValueKind != System.Text.Json.JsonValueKind.Object)
            return;

        foreach (var property in payload.EnumerateObject())
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(property.Name, propertyName) ||
                property.Value.ValueKind != System.Text.Json.JsonValueKind.String)
                continue;

            var value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                metadata[metadataKey] = value;
            return;
        }
    }

    private async ValueTask<SchedulerWorkDelivery?> AcquireNextAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        if (_schedulerWorkQueue.SupportsClaimTransitions && _schedulerWorkQueue.SupportsClaimValidation)
        {
            var claim = await _schedulerWorkQueue.ClaimAsync(
                new RuntimeSchedulerWorkClaimRequest(
                    workflowExecutionId,
                    _claimOwnerId,
                    _timeProvider.GetUtcNow(),
                    _claimOptions.VisibilityTimeout) { ValidateBeforeClaim = ValidateProtocolBeforeClaimAsync },
                cancellationToken);
            return claim is null ? null : new SchedulerWorkDelivery(claim.Item, claim);
        }

        // Legacy providers cannot run operation-local validation on their claim path. Validate the observed
        // head under the execution's single-writer lease before entering their unchanged claim/dequeue path.
        var page = await _schedulerWorkQueue.ListAsync(new RuntimeSchedulerWorkQuery(workflowExecutionId, limit: 1), cancellationToken);
        var item = page.Items.FirstOrDefault();
        if (item is not null)
            await ValidateProtocolBeforeClaimAsync(item, cancellationToken);
        if (_schedulerWorkQueue.SupportsClaimTransitions)
        {
            var claim = await _schedulerWorkQueue.ClaimAsync(new(workflowExecutionId, _claimOwnerId,
                _timeProvider.GetUtcNow(), _claimOptions.VisibilityTimeout), cancellationToken);
            return claim is null ? null : new SchedulerWorkDelivery(claim.Item, claim);
        }
        return item is null ? null : new SchedulerWorkDelivery(item, null);
    }

    private async ValueTask ValidateProtocolBeforeClaimAsync(RuntimeSchedulerWorkItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.CommandKind == WorkflowExecutionCommandKind.ExecuteModel && ModelProtocols is not null)
        {
            await ModelProtocols.SelectAsync(item, cancellationToken);
            return;
        }
        if (item.CommandKind == WorkflowExecutionCommandKind.ExecuteModel || !Enum.IsDefined(item.CommandKind) ||
            ModelProtocols?.IsSelectedModel(item.WorkflowExecutionId) == true)
            throw new RuntimeSchedulerWorkUnavailableException("runtime.model.protocol-unavailable",
                "Scheduler work requires a verified execution protocol that this host has not selected.");
    }

    // Completes the current fenced claim when the provider supports claim transitions. For a legacy provider, consumes
    // the FIFO head and enforces the single-writer TOCTOU tripwire: the consumed head MUST be the item that was
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
            // work already committed.
            if (_consumedWorkClaimAccessor?.WasConsumedDurably == true)
                return;

            // A consume that is still in flight only justifies skipping this one renewal, never ending the loop: the
            // commit can still fail and leave the item queued, and a dispatch that then ran on without renewing would
            // let the claim lapse and a successor re-drive work still in progress. Skipping costs at most one cadence
            // of visibility (the timeout is three cadences), and renewing into the store call that is deleting this
            // very document is the interleaving worth avoiding.
            if (IsOwnConsumeSettlingOrSettled(renewal.Current))
                continue;

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
                    continue;
                await dispatchCancellation.CancelAsync();
                throw NewClaimLost(renewal.Current, "renew", status: null, exception);
            }

            if (result.Status != RuntimeSchedulerWorkClaimTransitionStatus.Succeeded || result.Claim is null)
            {
                // Re-checked rather than keyed on the status: our own consume surfaces as AlreadyApplied or as Stale
                // depending on where the renewal's load and save fell around the delete, and the in-memory queue reports
                // a missing item as Stale in every case. A genuine successor still lands here and still wins. Looping
                // rather than returning lets the next cadence settle it: the durable check above ends the loop once the
                // consume lands, and a commit that failed instead gets renewed again.
                if (IsOwnConsumeSettlingOrSettled(renewal.Current))
                    continue;
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
        if (workItem.CommandKind == WorkflowExecutionCommandKind.ExecuteModel && ModelProtocols is not null)
            return new ModelProtocolWorkHandler(ModelProtocols);
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
