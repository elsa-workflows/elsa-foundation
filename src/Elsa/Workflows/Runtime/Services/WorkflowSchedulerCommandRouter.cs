using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowSchedulerCommandRouter : IWorkflowExecutionCommandExecutor
{
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IWorkflowSchedulerDrainPolicy _schedulerDrainPolicy;
    private readonly IWorkflowDrainOrchestrator _drainCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly IActivityExecutionStateStore? _activityExecutionStateStore;
    private readonly IWorkflowBurstScopeAccessor? _burstScopeAccessor;
    private readonly RuntimeBurstCacheOptions _burstCacheOptions;
    private readonly IWorkflowAlterationActorCommandExecutor? _alterationActorCommandExecutor;
    private readonly IRuntimeAdmissionController? _admissionController;

    public WorkflowSchedulerCommandRouter(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IWorkflowSchedulerDrainPolicy schedulerDrainPolicy,
        IWorkflowDrainOrchestrator drainCoordinator,
        TimeProvider? timeProvider = null,
        IActivityExecutionStateStore? activityExecutionStateStore = null,
        IWorkflowBurstScopeAccessor? burstScopeAccessor = null,
        RuntimeBurstCacheOptions? burstCacheOptions = null,
        IWorkflowAlterationActorCommandExecutor? alterationActorCommandExecutor = null,
        IRuntimeAdmissionController? admissionController = null)
    {
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(schedulerDrainPolicy);
        ArgumentNullException.ThrowIfNull(drainCoordinator);

        _schedulerWorkQueue = schedulerWorkQueue;
        _schedulerDrainPolicy = schedulerDrainPolicy;
        _drainCoordinator = drainCoordinator;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _activityExecutionStateStore = activityExecutionStateStore;
        _burstScopeAccessor = burstScopeAccessor;
        _burstCacheOptions = burstCacheOptions ?? new RuntimeBurstCacheOptions();
        _alterationActorCommandExecutor = alterationActorCommandExecutor;
        _admissionController = admissionController;
    }

    public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        return await ProcessAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);
    }

    public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(
        WorkflowExecutionCommandEnvelope envelope,
        WorkflowExecutionCommandDispatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(options);

        // Admission control (RB1, #1235; extended to AlterWorkflow by #1325). The gate sits at the very top of the
        // method, ahead of BOTH the alteration hand-off and the enqueue, for two reasons.
        //
        // It is the last point at which a refusal can still be honest: shedding after the work item is durably queued
        // would leave the recovery sweep to run it later, so a caller told "not taken, retry" would get its work done
        // twice.
        //
        // And the charge an admitted decision opens has to COVER the work the chosen branch then performs.
        // <c>RecordDispatch()</c> is <c>Ambient.Value?.Add()</c>, so a flow running with no charge open weighs
        // nothing, and a gate placed after a branch would leave that branch's dispatches invisible to the limiter.
        // For the alteration branch specifically that coverage is currently latent rather than load-bearing: the
        // executor reaches no <c>RecordDispatch()</c> call site — the only one in the product is in
        // <c>WorkflowSchedulerDrainer</c>, which only the drain branch below drives — so an admitted alteration
        // weighs exactly the seed unit for its duration, flat, whatever its plan size. What the coverage does buy
        // today is that a command dispatched from INSIDE an alteration sees an ambient charge and takes
        // <c>TryAdmit</c>'s nested-command exemption instead of being shed behind its own caller. The charge is an
        // <c>AsyncLocal</c>, so it flows down into everything this method awaits and is released only when this
        // <c>using</c> goes out of scope — after the alteration executor has returned, not before it is called.
        using var admission = IsSubjectToAdmission(envelope.Command.Kind) ? _admissionController?.TryAdmit() : null;
        if (admission is { IsAdmitted: false })
        {
            var queued = QueuesOnShed(envelope.Command.Kind);
            if (queued)
                await _schedulerWorkQueue.EnqueueAsync(await CreateWorkItemAsync(envelope, cancellationToken), cancellationToken);

            return WorkflowExecutionCommandProcessResult.FromShed(admission.Reason!, admission.RetryAfter, queued);
        }

        if (envelope.Command.Kind == WorkflowExecutionCommandKind.AlterWorkflow)
        {
            if (_alterationActorCommandExecutor is null)
                throw new InvalidOperationException("Runtime alterations are not composed for this runtime host.");
            await _alterationActorCommandExecutor.ExecuteAsync(envelope, cancellationToken);
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }

        var workItem = await _schedulerWorkQueue.EnqueueAsync(await CreateWorkItemAsync(envelope, cancellationToken), cancellationToken);

        var drainRequest = _schedulerDrainPolicy.CreateDrainRequest(envelope, workItem);
        if (drainRequest is null)
            return WorkflowExecutionCommandProcessResult.NoDrain;

        if (!string.Equals(drainRequest.WorkflowExecutionId, envelope.WorkflowExecutionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Scheduler drain policy returned workflow execution ID '{drainRequest.WorkflowExecutionId}' for command envelope workflow execution ID '{envelope.WorkflowExecutionId}'.");

        if (options.AmbientServices is not null)
            drainRequest = drainRequest.WithAmbientServices(options.AmbientServices);

        // Establish the burst-scoped reconstructible cache for this drain (ADR 0031 item b, spec 111). One command
        // envelope drives one drain-to-quiescence = one burst, spanning every drain cycle and both cadence branches
        // (Immediate/Coalesced) inside the orchestrator; two sequential drains of the same execution get separate
        // scopes, so cache entries never leak across drains. When the kill switch is off (or no accessor is wired) no
        // scope is pushed and every executable read takes the durable path — byte-identical to the burst-absent path.
        var burstScope = _burstScopeAccessor is not null && _burstCacheOptions.Enabled
            ? new WorkflowBurstScope(drainRequest.WorkflowExecutionId)
            : null;

        if (burstScope is null)
        {
            var plainResult = await _drainCoordinator.DrainAsync(envelope, drainRequest, cancellationToken);
            return WorkflowExecutionCommandProcessResult.FromDrain(plainResult);
        }

        using (_burstScopeAccessor!.Push(burstScope))
        {
            try
            {
                var drainResult = await _drainCoordinator.DrainAsync(envelope, drainRequest, cancellationToken);
                return WorkflowExecutionCommandProcessResult.FromDrain(drainResult);
            }
            finally
            {
                await burstScope.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Whether a command kind is <b>live work arriving at the host</b>, and so subject to admission control.
    /// Admission bounds the door; it must not be applied to the runtime recovering work it already accepted, nor to
    /// the commands an operator uses to reduce load.
    /// </summary>
    /// <remarks>
    /// <para><c>RunSchedulerWork</c> is the resumption sweep's re-drive envelope, and the backlog it names is already
    /// in the durable queue. Gating it would make every sweep pass at capacity enqueue one more trigger for work that
    /// is already queued, so sustained load would grow the backlog the sweep exists to drain — the same accumulation
    /// shape <c>RuntimeResumptionService</c> already documents for the terminal guard. The sweep carries its own
    /// bound (<c>RuntimeResumptionOptions.MaxExecutionsPerSweep</c>), which is the mechanism this one was ported
    /// from; it does not need a second one.</para>
    /// <para>Cancel, pause, and unpause are control-plane commands whose effect is to <em>reduce</em> load. Refusing
    /// an operator's cancel during overload would take away the tool for ending the overload, and they schedule
    /// almost no work themselves.</para>
    /// <para>This deny-list is the whole exemption set: every kind not named here reaches the gate, including
    /// <c>AlterWorkflow</c> (#1325). Its plan store admits by idempotency key, which is registration and not a load
    /// bound, so before it was gated an alteration held no charge at all and weighed zero while live traffic was being
    /// shed. Gating it makes it <em>sheddable</em> and worth exactly one unit — the seed every admitted command pays —
    /// for as long as its actor call runs. It is not worth more than that: the alteration executor performs no
    /// scheduler dispatches of its own, so it accrues none of the per-dispatch units a <c>Start</c> does, and the
    /// scheduler work it commits is drained later under the exempt <c>RunSchedulerWork</c> and stays uncounted. One
    /// counted, refusable unit is the whole gain, and it is a smaller gain than "alteration load is now metered".</para>
    /// </remarks>
    private static bool IsSubjectToAdmission(WorkflowExecutionCommandKind kind) => kind is not (
        WorkflowExecutionCommandKind.RunSchedulerWork or
        WorkflowExecutionCommandKind.Cancel or
        WorkflowExecutionCommandKind.PauseWorkflowExecution or
        WorkflowExecutionCommandKind.UnpauseWorkflowExecution);

    /// <summary>
    /// Whether a shed command parks its work item for a later drain, or is refused outright with nothing written.
    /// </summary>
    /// <remarks>
    /// <para>The shape follows from what would re-drive the parked item. A gated kind naming a live execution is
    /// deferred rather than dropped: the item stays queued for the resumption sweep to re-drive, which is what is
    /// meant to make <c>Deferred</c> a promise rather than a drop. <b>That sweep is not unconditional.</b> It exists
    /// only where <c>WorkflowsRuntimeResumptionFeature</c> is composed; <c>AddWorkflowRuntime</c> does not register
    /// it, while admission itself is registered for every host and on by default. On a composition without a
    /// re-driver the parked item has no owner and the promise is not kept — the open gap tracked by #1320, whose
    /// chosen resolution (degrade to the start-shaped refusal when no re-driver is composed) is not implemented
    /// here.</para>
    /// <para><c>Start</c> has no execution id to hand back, so it is refused outright and nothing durable is written;
    /// the HTTP edge renders that as 429 with Retry-After. Queueing it would let the sweep run it later, so a caller
    /// told "not taken, retry" would get the work done twice.</para>
    /// <para><c>AlterWorkflow</c> must not queue either, for a different reason: no drain handler would ever run it.
    /// <c>NoopWorkflowSchedulerWorkHandler.CanHandle</c> matches every kind except <c>InvokeActivity</c>,
    /// <c>GeneratedEvent</c>, and <c>ResumeBookmark</c>, and its <c>HandleAsync</c> returns without doing anything, so
    /// a parked alteration item would be silently swallowed on the next drain even on a host with the resumption sweep
    /// composed. That property is not unique to it: <c>ContinueVolatileWait</c> and <c>DeliverSignal</c> have no
    /// handler either and would be swallowed the same way — latent only because nothing in <c>Elsa</c> constructs
    /// them, so whoever wires one up owns adding it here. <c>AlterWorkflow</c> is named on this list rather than the
    /// whole handler-less set because it is the one that is actually reachable, and because it is the one with a
    /// re-driver that makes the outright refusal safe: <c>WorkflowAlterationOrchestrationPumpTask</c> is registered by
    /// <c>AddWorkflowRuntime</c> itself rather than by any opt-in feature, so no composition choice can drop it —
    /// though as an <c>IRecurringTask</c> it still only runs where the Tasks domain's <c>TaskManager</c> schedules it,
    /// and that same pump is the only producer of <c>AlterWorkflow</c> envelopes, so a host that does not run it
    /// cannot reach this refusal in the first place. A refused dispatch writes nothing and leaves the job claimable,
    /// and <c>IWorkflowAlterationStore.ClaimNextAsync</c> re-claims running jobs once their lease lapses — a full
    /// <c>WorkflowAlterationOrchestrationOptions.JobLeaseDuration</c>, one minute by default, against a
    /// <c>RetryAfter</c> measured in seconds, so a large campaign under sustained load re-drives on minutes.</para>
    /// </remarks>
    private static bool QueuesOnShed(WorkflowExecutionCommandKind kind) => kind is not (
        WorkflowExecutionCommandKind.Start or
        WorkflowExecutionCommandKind.AlterWorkflow);

    private async ValueTask<RuntimeSchedulerWorkItem> CreateWorkItemAsync(
        WorkflowExecutionCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var executionScopeId = await ResolveExecutionScopeIdAsync(envelope, cancellationToken);
        return new RuntimeSchedulerWorkItem(
            workItemId: envelope.EnvelopeId,
            workflowExecutionId: envelope.WorkflowExecutionId,
            commandId: envelope.Command.CommandId,
            commandKind: envelope.Command.Kind,
            envelopeId: envelope.EnvelopeId,
            idempotencyKey: envelope.IdempotencyKey,
            enqueuedAt: envelope.EnqueuedAt,
            recordedAt: _timeProvider.GetUtcNow(),
            sequence: envelope.Sequence,
            payload: envelope.Command.Payload,
            commandMetadata: envelope.Command.Metadata,
            envelopeMetadata: envelope.Metadata,
            executionScopeId: executionScopeId);
    }

    private async ValueTask<string?> ResolveExecutionScopeIdAsync(
        WorkflowExecutionCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (_activityExecutionStateStore is null || envelope.Command.Kind != WorkflowExecutionCommandKind.ResumeBookmark)
            return null;
        if (!envelope.Command.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityExecutionId, out var activityExecutionId) ||
            string.IsNullOrWhiteSpace(activityExecutionId))
        {
            return null;
        }

        var state = await _activityExecutionStateStore.FindAsync(envelope.WorkflowExecutionId, activityExecutionId, cancellationToken);
        return state?.ExecutionScopeId ?? state?.Provenance.ExecutionScopeId;
    }
}
