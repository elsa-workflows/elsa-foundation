using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Builds deterministic at-least-once actor commands for claimed alteration jobs.</summary>
public sealed class WorkflowAlterationJobDispatcher(
    IWorkflowExecutionActorProvider actorProvider,
    TimeProvider? timeProvider = null) : IWorkflowAlterationJobDispatcher
{
    private readonly IWorkflowExecutionActorProvider _actorProvider = actorProvider ?? throw new ArgumentNullException(nameof(actorProvider));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<WorkflowAlterationActorDispatchResult> DispatchAsync(
        WorkflowAlterationJobState job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Status != WorkflowAlterationJobStatus.Running || job.Claim is null)
            throw new ArgumentException("Only a claimed running alteration job can be dispatched to an actor.", nameof(job));

        var now = _timeProvider.GetUtcNow();
        var payload = new WorkflowAlterationActorCommand(job.JobId, job.Claim.Token);
        payload.Validate();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.alteration.jobId"] = job.JobId,
            ["runtime.alteration.planId"] = job.PlanId
        };
        var command = new WorkflowExecutionCommand(
            $"{job.JobId}:command:alter",
            job.WorkflowExecutionId,
            WorkflowExecutionCommandKind.AlterWorkflow,
            now,
            JsonSerializer.SerializeToElement(payload),
            metadata);
        var partition = new WorkflowExecutionPartition(job.TenantPartition);
        var envelope = new WorkflowExecutionCommandEnvelope(
            $"{job.JobId}:envelope:alter",
            job.WorkflowExecutionId,
            command,
            $"{job.JobId}:alter",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            now,
            metadata: metadata,
            partition: partition);
        var actor = await _actorProvider.GetAgentAsync(
            new WorkflowExecutionActorActivationRequest(
                job.WorkflowExecutionId,
                WorkflowExecutionActorActivationReason.ControlPlaneCommand,
                now,
                "runtime-alteration-worker",
                WorkflowExecutionActorCapabilities.None,
                metadata,
                partition),
            cancellationToken);
        var dispatch = await actor.EnqueueAsync(envelope, cancellationToken);
        return new WorkflowAlterationActorDispatchResult(job.JobId, dispatch, actor.Descriptor);
    }
}
