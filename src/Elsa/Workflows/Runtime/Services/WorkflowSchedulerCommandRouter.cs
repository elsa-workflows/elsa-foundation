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
        // nothing; a gate placed after a branch would leave that branch's dispatches invisible to the limiter, which
        // is exactly what made alteration load uncounted before #1325. The charge is an <c>AsyncLocal</c>, so it flows
        // down into everything this method awaits and is released only when this <c>using</c> goes out of scope —
        // after the alteration executor has returned, not before it is called.
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
    /// bound, so before it was gated an alteration held no charge and every unit of work it ran weighed zero against
    /// the limiter while live traffic was being shed.</para>
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
    /// deferred rather than dropped: the item stays queued and the resumption sweep re-drives it, which is what makes
    /// <c>Deferred</c> a promise rather than a lie.</para>
    /// <para><c>Start</c> has no execution id to hand back, so it is refused outright and nothing durable is written;
    /// the HTTP edge renders that as 429 with Retry-After. Queueing it would let the sweep run it later, so a caller
    /// told "not taken, retry" would get the work done twice.</para>
    /// <para><c>AlterWorkflow</c> must not queue either, for a different reason: no drain handler would ever run it.
    /// <c>NoopWorkflowSchedulerWorkHandler.CanHandle</c> matches every kind except <c>InvokeActivity</c>,
    /// <c>GeneratedEvent</c>, and <c>ResumeBookmark</c>, and its <c>HandleAsync</c> returns without doing anything, so
    /// a parked alteration item would be silently swallowed on the next drain even on a host with the resumption sweep
    /// composed. The outright refusal is safe because the alteration path carries its own re-driver, registered
    /// unconditionally in <c>AddWorkflowRuntime</c>: a refused dispatch writes nothing, leaves the job claimable, and
    /// <c>IWorkflowAlterationStore.ClaimNextAsync</c> re-claims running jobs once their lease lapses.</para>
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
