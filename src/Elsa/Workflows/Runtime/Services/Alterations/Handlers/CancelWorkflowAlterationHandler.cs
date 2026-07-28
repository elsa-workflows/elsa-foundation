using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>Trusted <c>CancelWorkflow/1</c> implementation. Its only effect is checkpoint-bound cancellation staging.</summary>
public sealed class CancelWorkflowAlterationHandler(
    WorkflowCancellationPlanner cancellationPlanner) : IWorkflowAlterationPreflightHandler
{
    private readonly WorkflowCancellationPlanner _cancellationPlanner = cancellationPlanner ?? throw new ArgumentNullException(nameof(cancellationPlanner));

    public async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsEmptyPayload(context.Envelope.Payload))
            return WorkflowAlterationPreflightResult.Rejected("InvalidCancelPayload", "CancelWorkflow does not accept a payload.");

        var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
        var activities = context.ProjectedState.GetRequired<WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection>();
        var checkpointId = $"checkpoint:alteration:{context.Job.JobId}:{context.Job.AttemptCount}";
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.CheckpointReason] = "WorkflowCancellation",
            ["runtime.alteration.jobId"] = context.Job.JobId,
            ["runtime.alteration.planId"] = context.Job.PlanId
        };
        var plan = await _cancellationPlanner.PlanAsync(workflow, activities.Activities, checkpointId, metadata, cancellationToken);
        if (plan is not null)
            context.Staging.Stage(new CancelWorkflowStagedChange(plan));
        return WorkflowAlterationPreflightResult.Accepted();
    }

    private static bool IsEmptyPayload(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && !payload.EnumerateObject().Any();

    private sealed record CancelWorkflowStagedChange(WorkflowCancellationPlan Plan) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public string ChangeKey => "runtime.alteration.cancel-workflow";

        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.SetWorkflowExecution(Plan.WorkflowExecution);
            foreach (var activity in Plan.ActivityExecutions)
                builder.AddActivityExecution(activity);
            foreach (var inspection in Plan.ActivityExecutionInspections)
                builder.AddActivityExecutionInspection(inspection);
            foreach (var cleanup in Plan.CleanupRequests)
                builder.AddActivityScopeCleanup(cleanup);
        }
    }
}
