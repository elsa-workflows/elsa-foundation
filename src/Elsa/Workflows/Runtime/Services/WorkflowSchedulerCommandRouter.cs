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

        if (envelope.Command.Kind == WorkflowExecutionCommandKind.AlterWorkflow)
        {
            if (_alterationActorCommandExecutor is null)
                throw new InvalidOperationException("Runtime alterations are not composed for this runtime host.");
            await _alterationActorCommandExecutor.ExecuteAsync(envelope, cancellationToken);
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }

        var executionScopeId = await ResolveExecutionScopeIdAsync(envelope, cancellationToken);
        var workItem = new RuntimeSchedulerWorkItem(
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

        // Admission control (RB1, #1235). The gate sits here, ahead of the enqueue, because that is the last point at
        // which a refusal can still be honest. Shedding after the work item is durably queued would leave the recovery
        // sweep to run it later, so a caller told "not taken, retry" would get its work done twice.
        //
        // The two refusal shapes follow from what the caller can correlate against. A start has no execution id to
        // hand back, so it is refused outright and nothing durable is written; the HTTP edge renders that as 429 with
        // Retry-After. Every other kind already names a live execution, so the work item IS queued and only the drain
        // is skipped: the resumption sweep re-drives it, which is what makes Deferred a promise rather than a drop.
        using var admission = _admissionController?.TryAdmit();
        if (admission is { IsAdmitted: false })
        {
            if (envelope.Command.Kind != WorkflowExecutionCommandKind.Start)
                await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);

            return WorkflowExecutionCommandProcessResult.FromShed(admission.Reason!, admission.RetryAfter);
        }

        workItem = await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);

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
