using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// Submits operator alterations the way the runtime API does (<c>DecideIntervention/1</c>, <c>RetryFaultedActivity/1</c>,
/// <c>CancelWorkflow/1</c>), then runs the alteration pump until the job is terminal and the resumption sweeps until the
/// run is quiet. A host that stops during the sweeps ends the call quietly when the caller allows it.
/// </summary>
public static class OperatorActions
{
    public const string Operator = OperatorRetry.Operator;

    /// <summary>The authority the harness starts runs under.</summary>
    public const string HarnessAuthority = "activity-behavioural-drive";

    public static Task<WorkflowAlterationPlanAdmissionResult> SubmitDecisionAsync(
        WorkflowExecutionHarness harness,
        string workflowExecutionId,
        string incidentId,
        string decision,
        string idempotencyKey,
        WorkflowExecutionPartition partition,
        string reason = "Operator decision",
        string rootInitiator = HarnessAuthority) =>
        SubmitAsync(harness, workflowExecutionId, new WorkflowAlterationEnvelope(
            DecideInterventionAlterationHandler.Kind,
            DecideInterventionAlterationHandler.SchemaVersion,
            JsonSerializer.SerializeToElement(new { incidentId, decision, reason })), idempotencyKey, partition, rootInitiator);

    public static async Task<WorkflowAlterationJobState> DecideAsync(
        WorkflowExecutionHarness harness,
        string workflowExecutionId,
        string incidentId,
        string decision,
        string idempotencyKey,
        WorkflowExecutionPartition partition,
        string reason = "Operator decision",
        bool hostMayStop = false,
        string rootInitiator = HarnessAuthority)
    {
        var admission = await SubmitDecisionAsync(harness, workflowExecutionId, incidentId, decision, idempotencyKey, partition, reason, rootInitiator);
        return await OrchestrateAsync(harness, workflowExecutionId, admission.Plan.PlanId, hostMayStop);
    }

    public static async Task<WorkflowAlterationJobState> RetryAsync(
        WorkflowExecutionHarness harness,
        string workflowExecutionId,
        string sourceActivityExecutionId,
        string idempotencyKey,
        WorkflowExecutionPartition partition)
    {
        var admission = await SubmitAsync(harness, workflowExecutionId, new WorkflowAlterationEnvelope(
            RetryFaultedActivityAlterationHandler.Kind,
            RetryFaultedActivityAlterationHandler.SchemaVersion,
            JsonSerializer.SerializeToElement(new { sourceActivityExecutionId, reason = "Participant restored" })), idempotencyKey, partition);
        return await OrchestrateAsync(harness, workflowExecutionId, admission.Plan.PlanId);
    }

    public static async Task<WorkflowAlterationJobState> CancelAsync(
        WorkflowExecutionHarness harness,
        string workflowExecutionId,
        string idempotencyKey,
        WorkflowExecutionPartition partition)
    {
        var admission = await SubmitAsync(harness, workflowExecutionId,
            new WorkflowAlterationEnvelope("CancelWorkflow", 1, JsonSerializer.SerializeToElement(new { })), idempotencyKey, partition);
        return await OrchestrateAsync(harness, workflowExecutionId, admission.Plan.PlanId);
    }

    public static async Task<WorkflowAlterationPlanAdmissionResult> SubmitAsync(
        WorkflowExecutionHarness harness,
        string workflowExecutionId,
        WorkflowAlterationEnvelope envelope,
        string idempotencyKey,
        WorkflowExecutionPartition partition,
        string rootInitiator = HarnessAuthority)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
            new WorkflowAlterationSubmission(
                new WorkflowAlterationAuthorityScope(partition.Value, HarnessAuthority, rootInitiator),
                WorkflowAlterationTargetSelector.ForExecutionIds([workflowExecutionId]),
                [envelope]),
            new WorkflowAlterationOperatorProvenance(Operator, correlationId: null),
            idempotencyKey);
    }

    public static async Task<WorkflowAlterationJobState> OrchestrateAsync(
        WorkflowExecutionHarness harness,
        string workflowExecutionId,
        string planId,
        bool hostMayStop = false)
    {
        var jobId = WorkflowAlterationIdentity.CreateJobId(planId, workflowExecutionId);
        var options = new WorkflowAlterationOrchestrationOptions { WorkerId = "test-worker" };
        WorkflowAlterationJobState? job = null;
        for (var sweep = 0; sweep < 8; sweep++)
        {
            await using (var scope = harness.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<WorkflowAlterationOrchestrationSweep>().ExecuteAsync(options, "test-worker");
                job = await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId);
            }

            if (job?.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled)
                break;
        }

        Assert.NotNull(job);
        try
        {
            await harness.SweepUntilQuietAsync();
        }
        catch (Exception exception) when (hostMayStop && exception is OperationCanceledException or InvalidOperationException)
        {
            // The host stopped during the sweeps, as a process shutdown stops it.
        }

        return job!;
    }
}
