using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>
/// Trusted <c>ScheduleActivity/1</c> implementation. The request names only a compiled child node and its live
/// direct parent; the activity module derives all execution identity, scope, provenance, and input materialization.
/// </summary>
public sealed class ScheduleActivityAlterationHandler(
    IWorkflowExecutableStore executableStore,
    IOperatorActivitySchedulingPolicyRegistry policyRegistry,
    TimeProvider timeProvider) : IWorkflowAlterationPreflightHandler
{
    private readonly IWorkflowExecutableStore _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));
    private readonly IOperatorActivitySchedulingPolicyRegistry _policyRegistry = policyRegistry ?? throw new ArgumentNullException(nameof(policyRegistry));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryReadPayload(context.Envelope.Payload, out var nodeId, out var parentId))
            return WorkflowAlterationPreflightResult.Rejected("InvalidScheduleActivityPayload", "ScheduleActivity requires nodeId and parentActivityExecutionId.");

        var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
        if (workflow.Status.IsTerminal())
            return WorkflowAlterationPreflightResult.Rejected("WorkflowTerminal", "A terminal workflow cannot schedule an activity.");
        if (WorkflowAlterationCapturedConcurrencyValidator.ValidateWorkflow(context.Job, workflow) is { } workflowConflict)
            return WorkflowAlterationPreflightResult.Rejected(workflowConflict.Code, workflowConflict.Message);

        var executable = await _executableStore.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null || !Equals(executable.Identity, workflow.PinnedExecutable))
            return WorkflowAlterationPreflightResult.Rejected("PinnedExecutableUnavailable", "The pinned workflow executable is unavailable.");
        if (!executable.NodesById.TryGetValue(nodeId, out var child))
            return WorkflowAlterationPreflightResult.Rejected("ExecutableNodeNotFound", "The requested executable node does not exist.");

        var activities = context.ProjectedState.GetRequired<WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection>().Activities;
        var parent = activities.SingleOrDefault(state => StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, parentId));
        if (parent is null)
            return WorkflowAlterationPreflightResult.Rejected("ParentActivityNotFound", "The requested parent activity does not exist.");
        if (WorkflowAlterationCapturedConcurrencyValidator.ValidateActivity(context.Job, parent, "parent") is { } parentConflict)
            return WorkflowAlterationPreflightResult.Rejected(parentConflict.Code, parentConflict.Message);
        if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var parentNode))
            return WorkflowAlterationPreflightResult.Rejected("ParentExecutableNodeNotFound", "The requested parent is not part of the pinned executable.");

        var relation = parentNode.ChildSlots.SingleOrDefault(slot => slot.Activities.Any(candidate => StringComparer.Ordinal.Equals(candidate.ExecutableNodeId, child.ExecutableNodeId)));
        if (relation is null)
            return WorkflowAlterationPreflightResult.Rejected("ChildRelationNotFound", "The requested node is not a direct child of the requested parent.");
        if (relation.OperatorSchedulingCapability is not { } capability)
            return WorkflowAlterationPreflightResult.Rejected("OperatorSchedulingNotSupported", "The requested child relation is not operator-schedulable.");

        var policy = _policyRegistry.Find(capability.PolicyKey, capability.SchemaVersion);
        if (policy is null)
            return WorkflowAlterationPreflightResult.Rejected("OperatorSchedulingPolicyUnavailable", "The compiled scheduling policy is not available in this runtime.");
        var decision = policy.Evaluate(new OperatorActivitySchedulingPolicyContext(workflow, parent, child, capability, activities));
        if (!decision.IsAccepted)
            return WorkflowAlterationPreflightResult.Rejected(decision.Code ?? "OperatorSchedulingRejected", decision.Message);

        var reservations = context.ProjectedState.TryGet<WorkflowAlterationScheduleReservationProjection>(out var existingReservations)
            ? existingReservations!
            : new WorkflowAlterationScheduleReservationProjection();
        if (!reservations.TryReserve(parent.Execution.ActivityExecutionId, child.ExecutableNodeId))
            return WorkflowAlterationPreflightResult.Rejected("ScheduleActivitySlotConflict", "The requested activity slot is already scheduled by this alteration job.");
        context.ProjectedState.Set(reservations);

        var activityExecutionId = RuntimeChainId.Derive(context.Job.JobId, $"alteration-schedule:{context.Ordinal}:{child.ExecutableNodeId}");
        var now = _timeProvider.GetUtcNow();
        var provenance = decision.Provenance!;
        var payload = new RuntimeScheduleActivityCommandPayload(
            workflow.PinnedExecutable,
            child.ExecutableNodeId,
            activityExecutionId,
            "OperatorScheduleActivity",
            schedulingActivityExecutionId: provenance.SchedulingActivityExecutionId,
            parentActivityExecutionId: provenance.ParentActivityExecutionId,
            schedulingProvenance: provenance);
        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(context.Job.JobId, $"alteration-schedule-work:{context.Ordinal}:{child.ExecutableNodeId}"),
            workflowExecutionId: workflow.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(context.Job.JobId, $"alteration-schedule-command:{context.Ordinal}:{child.ExecutableNodeId}"),
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: context.Job.JobId,
            idempotencyKey: RuntimeChainId.Derive(context.Job.JobId, $"alteration-schedule-idempotency:{context.Ordinal}:{child.ExecutableNodeId}"),
            enqueuedAt: now,
            recordedAt: now,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime.alteration.jobId"] = context.Job.JobId,
                ["runtime.alteration.planId"] = context.Job.PlanId,
                ["runtime.alteration.kind"] = context.Envelope.Kind
            },
            envelopeMetadata: new Dictionary<string, string>(StringComparer.Ordinal),
            executionScopeId: provenance.ExecutionScopeId,
            attempt: provenance.Attempt);
        var intent = new RuntimePostCommitIntent(
            intentId: $"alteration:{context.Job.JobId}:schedule:{context.Ordinal}:{child.ExecutableNodeId}",
            workflowExecutionId: workflow.WorkflowExecutionId,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: now,
            activityExecutionId: parent.Execution.ActivityExecutionId,
            idempotencyKey: workItem.IdempotencyKey,
            payload: JsonSerializer.SerializeToElement(workItem),
            metadata: workItem.CommandMetadata,
            materializedSchedulerWorkItem: workItem);
        context.Staging.Stage(new ScheduleActivityStagedChange(intent));
        return WorkflowAlterationPreflightResult.Accepted();
    }

    private static bool TryReadPayload(JsonElement payload, out string nodeId, out string parentId)
    {
        nodeId = string.Empty;
        parentId = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("nodeId", out var node) || node.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("parentActivityExecutionId", out var parent) || parent.ValueKind != JsonValueKind.String ||
            payload.EnumerateObject().Any(property => property.Name is not "nodeId" and not "parentActivityExecutionId"))
            return false;
        nodeId = node.GetString()?.Trim() ?? string.Empty;
        parentId = parent.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(nodeId) && !string.IsNullOrWhiteSpace(parentId);
    }

    private sealed record ScheduleActivityStagedChange(RuntimePostCommitIntent Intent) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public string ChangeKey => $"runtime.alteration.schedule:{Intent.IntentId}";
        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder) => builder.AddPostCommitIntent(Intent);
    }
}

/// <summary>Job-local reservations make later preflights observe earlier accepted schedule requests.</summary>
internal sealed class WorkflowAlterationScheduleReservationProjection
{
    private readonly HashSet<(string ParentActivityExecutionId, string ExecutableNodeId)> _reserved = [];

    public bool TryReserve(string parentActivityExecutionId, string executableNodeId) =>
        _reserved.Add((parentActivityExecutionId, executableNodeId));
}
