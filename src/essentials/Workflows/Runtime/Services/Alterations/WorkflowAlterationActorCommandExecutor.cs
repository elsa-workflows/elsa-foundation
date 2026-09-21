using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Actor-only boundary for a claimed alteration job. The transport command carries only job/claim identifiers; this
/// executor resolves protected plan envelopes and the target workflow state after the actor has serialized the target.
/// </summary>
public sealed class WorkflowAlterationActorCommandExecutor(
    IWorkflowAlterationStore alterationStore,
    IWorkflowAlterationPayloadProtector payloadProtector,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    WorkflowAlterationJobExecutor jobExecutor,
    IActivityExecutionStateStore? activityExecutionStateStore = null,
    IWorkflowSchedulerWorkQueue? schedulerWorkQueue = null,
    TimeProvider? timeProvider = null) : IWorkflowAlterationActorCommandExecutor
{
    private readonly IWorkflowAlterationStore _alterationStore = alterationStore ?? throw new ArgumentNullException(nameof(alterationStore));
    private readonly IWorkflowAlterationPayloadProtector _payloadProtector = payloadProtector ?? throw new ArgumentNullException(nameof(payloadProtector));
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore = workflowExecutionStateStore ?? throw new ArgumentNullException(nameof(workflowExecutionStateStore));
    private readonly IActivityExecutionStateStore? _activityExecutionStateStore = activityExecutionStateStore;
    private readonly IWorkflowSchedulerWorkQueue? _schedulerWorkQueue = schedulerWorkQueue;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly WorkflowAlterationJobExecutor _jobExecutor = jobExecutor ?? throw new ArgumentNullException(nameof(jobExecutor));

    public async ValueTask ExecuteAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Command.Kind != WorkflowExecutionCommandKind.AlterWorkflow)
            throw new ArgumentException("The alteration actor executor only accepts AlterWorkflow commands.", nameof(envelope));
        if (envelope.Command.Payload is not { } payload)
            throw new ArgumentException("An AlterWorkflow command requires an actor payload.", nameof(envelope));

        var actorCommand = payload.Deserialize<WorkflowAlterationActorCommand>()
            ?? throw new ArgumentException("The AlterWorkflow command payload is invalid.", nameof(envelope));
        actorCommand.Validate();

        var job = await _alterationStore.FindJobAsync(actorCommand.JobId, cancellationToken)
            ?? throw new InvalidOperationException("The alteration job was not found.");
        if (!StringComparer.Ordinal.Equals(job.WorkflowExecutionId, envelope.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(job.TenantPartition, envelope.Partition.Value))
        {
            throw new InvalidOperationException("The alteration command does not belong to this workflow actor partition.");
        }

        // A duplicate after an acknowledgement loss reaches the same actor. The checkpoint marker is authoritative;
        // re-running handlers would be unsafe and is deliberately avoided.
        if (job.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled)
            return;
        if (job.Status != WorkflowAlterationJobStatus.Running || job.Claim is null ||
            !StringComparer.Ordinal.Equals(job.Claim.Token, actorCommand.ClaimToken))
        {
            throw new WorkflowAlterationClaimFenceException(job.JobId);
        }

        var plan = await _alterationStore.FindPlanAsync(job.PlanId, cancellationToken)
            ?? throw new InvalidOperationException("The alteration plan was not found.");
        if (!StringComparer.Ordinal.Equals(plan.AuthorityScope.TenantPartition, job.TenantPartition))
            throw new InvalidOperationException("The alteration plan tenant does not match its claimed target job.");

        var workflow = await _workflowExecutionStateStore.FindAsync(job.WorkflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException("The targeted workflow execution was not found.");
        var workflowPartition = workflow.Partition?.Value ?? WorkflowExecutionPartition.DefaultValue;
        if (!StringComparer.Ordinal.Equals(workflowPartition, job.TenantPartition))
            throw new InvalidOperationException("The targeted workflow execution tenant does not match the alteration job.");

        var plaintext = _payloadProtector.Unprotect(
            plan.PlanId,
            plan.AuthorityScope.TenantPartition,
            plan.CanonicalRequestHash,
            plan.ProtectedPayload);
        var envelopes = ReadEnvelopes(plaintext);
        var activities = _activityExecutionStateStore is null
            ? []
            : await _activityExecutionStateStore.ListAllAsync(workflow.WorkflowExecutionId, cancellationToken);
        var activeClaims = _schedulerWorkQueue is IWorkflowSchedulerWorkClaimInspection claimInspection
            ? await claimInspection.ListActiveClaimsAsync(workflow.WorkflowExecutionId, _timeProvider.GetUtcNow(), cancellationToken)
            : [];
        var projectedState = new WorkflowAlterationProjectedState([workflow, new WorkflowAlterationActivityExecutionProjection(activities, activeClaims)]);
        await _jobExecutor.ExecuteAsync(
            new WorkflowAlterationJobExecutionRequest(job, envelopes, projectedState),
            cancellationToken);
    }

    /// <summary>Actor-loaded activity view available to alteration handlers during no-I/O staging decisions.</summary>
    public sealed class WorkflowAlterationActivityExecutionProjection(
        IReadOnlyCollection<ActivityExecutionState> activities,
        IReadOnlyCollection<RuntimeSchedulerWorkClaim>? activeSchedulerClaims = null)
    {
        public IReadOnlyList<ActivityExecutionState> Activities { get; } = (activities ?? throw new ArgumentNullException(nameof(activities)))
            .OrderBy(state => state.ExecutionSequence)
            .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();

        public IReadOnlyList<RuntimeSchedulerWorkClaim> ActiveSchedulerClaims { get; } = (activeSchedulerClaims ?? [])
            .OrderBy(claim => claim.Item.RecordedAt)
            .ThenBy(claim => claim.Item.WorkItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<WorkflowAlterationEnvelope> ReadEnvelopes(string canonicalPlanJson)
    {
        using var document = JsonDocument.Parse(canonicalPlanJson);
        if (!document.RootElement.TryGetProperty("alterations", out var alterations) || alterations.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The protected alteration plan payload has no alteration envelopes.");

        var result = new List<WorkflowAlterationEnvelope>();
        foreach (var item in alterations.EnumerateArray())
        {
            if (!item.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out var schemaVersion) ||
                !item.TryGetProperty("payload", out var payload))
            {
                throw new InvalidOperationException("The protected alteration plan contains an invalid envelope.");
            }

            result.Add(new WorkflowAlterationEnvelope(kind.GetString()!, schemaVersion, payload));
        }

        if (result.Count == 0)
            throw new InvalidOperationException("The protected alteration plan contains no envelopes.");
        return result;
    }
}
