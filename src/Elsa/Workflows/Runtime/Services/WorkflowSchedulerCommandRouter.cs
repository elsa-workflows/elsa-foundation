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

        // Admission control (RB1, #1235; extended to AlterWorkflow by #1325), above BOTH the enqueue and the
        // alteration hand-off. Above the enqueue because this is the last point at which a refusal is honest: shed
        // after the work item is durably queued and the recovery sweep runs it later, so a caller told "not taken,
        // retry" gets its work done twice. Above the hand-off because the charge must COVER what the chosen branch
        // then does — RecordDispatch() is Ambient.Value?.Add() and TryAdmit's nested-command exemption reads that same
        // ambient value, so a gate below a branch leaves its dispatches uncounted and its nested commands shed behind
        // their own caller. Both are LATENT on the alteration path today (the executor reaches no RecordDispatch()
        // call site — the product's only one is in WorkflowSchedulerDrainer, driven only by the drain branch — and
        // dispatches no nested command), so the gate is hoisted for placement, not for a present gain. The charge is
        // an AsyncLocal, released only when this `using` goes out of scope: after the executor returns, not before.
        using var admission = IsSubjectToAdmission(envelope.Command.Kind) ? _admissionController?.TryAdmit() : null;
        if (admission is { IsAdmitted: false })
        {
            var queued = QueuesOnShed(envelope.Command.Kind);
            if (queued)
            {
                var shedWorkItem = await CreateWorkItemAsync(envelope, cancellationToken);
                await _schedulerWorkQueue.EnqueueAsync(shedWorkItem, cancellationToken);
            }

            return WorkflowExecutionCommandProcessResult.FromShed(admission.Reason!, admission.RetryAfter, queued);
        }

        if (envelope.Command.Kind == WorkflowExecutionCommandKind.AlterWorkflow)
        {
            // Below the gate, so at capacity such a host answers a retryable refusal instead of this composition
            // error. Nil impact: one registration composes both this executor and the pump that is its only producer.
            if (_alterationActorCommandExecutor is null)
                throw new InvalidOperationException("Runtime alterations are not composed for this runtime host.");
            await _alterationActorCommandExecutor.ExecuteAsync(envelope, cancellationToken);
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }

        var enqueuedWorkItem = await CreateWorkItemAsync(envelope, cancellationToken);
        var workItem = await _schedulerWorkQueue.EnqueueAsync(enqueuedWorkItem, cancellationToken);

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
    /// bound, so before it was gated an alteration held no charge and weighed zero while live traffic was being shed.
    /// Gating makes it <em>sheddable</em> and worth exactly one unit — the seed every admitted command pays — for as
    /// long as its actor call runs, and no more, because the executor performs no scheduler dispatches of its own.
    /// The work it commits is charged to whoever drains that execution next, never to the alteration: uncounted under
    /// the exempt <c>RunSchedulerWork</c>, and charged to <em>that</em> command under a subsequently admitted live
    /// one. One counted, refusable unit is the whole gain — smaller than "alteration load is now metered".</para>
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
    /// <para>The rule is what would re-drive the parked item. A gated kind naming a live execution is parked for the
    /// resumption sweep, which is what is meant to make <c>Deferred</c> a promise rather than a drop — a promise kept
    /// only where <c>WorkflowsRuntimeResumptionFeature</c> is composed, which <c>AddWorkflowRuntime</c> does not do:
    /// the open gap tracked by <b>#1320</b>, whose chosen resolution (degrade to the start-shaped refusal when no
    /// re-driver is composed) is not implemented here. The two kinds below are refused outright instead, because for
    /// them parking is worse than writing nothing: a queued <c>Start</c> would be run later by the sweep, so a caller
    /// told "not taken, retry" would get the work done twice, and a queued <c>AlterWorkflow</c> would be matched only
    /// by <c>NoopWorkflowSchedulerWorkHandler</c> and silently swallowed. Both have a re-driver that needs no parked
    /// item: the caller's retry, and the alteration pump re-claiming the job once its lease lapses.</para>
    /// <para>That swallow is systemic rather than an <c>AlterWorkflow</c> curiosity: <c>ContinueVolatileWait</c> and
    /// <c>DeliverSignal</c> have no handler anywhere and no faulting fallback either, and <c>NotifyParentActivity</c>
    /// shares that only on a host composing no <c>ActivitiesRuntimeFeature</c>, which registers its handler. So
    /// whoever makes one of them reachable as a router envelope owns deciding whether it belongs here — for the first
    /// two the answer is yes on any host, for the third only where that feature is absent. <c>AlterWorkflow</c> is
    /// named because it is the reachable one today. The <c>Noop</c> matching rule, the pump's registration and its <c>IRecurringTask</c>
    /// caveat, and the lease arithmetic live in the admission entry of <c>EXTENSION_POINTS.md</c>.</para>
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
