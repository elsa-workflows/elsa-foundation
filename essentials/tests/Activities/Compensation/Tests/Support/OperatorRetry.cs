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
/// Submits <c>RetryFaultedActivity/1</c> for a faulted activity the way an operator does, then runs the alteration
/// pump until the job is terminal and the resumption sweeps until the run is quiet.
/// </summary>
public static class OperatorRetry
{
    public const string Operator = "operator-1";

    public static async Task<WorkflowAlterationJobState> RetryAsync(
        WorkflowExecutionHarness harness,
        string workflowExecutionId,
        string sourceActivityExecutionId,
        string idempotencyKey,
        WorkflowExecutionPartition partition)
    {
        WorkflowAlterationPlanAdmissionResult admission;
        await using (var scope = harness.Services.CreateAsyncScope())
        {
            admission = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
                new WorkflowAlterationSubmission(
                    // The harness starts runs under this authority, and an alteration only captures runs of its own authority.
                    new WorkflowAlterationAuthorityScope(partition.Value, "activity-behavioural-drive", "activity-behavioural-drive"),
                    WorkflowAlterationTargetSelector.ForExecutionIds([workflowExecutionId]),
                    [new WorkflowAlterationEnvelope(RetryFaultedActivityAlterationHandler.Kind, RetryFaultedActivityAlterationHandler.SchemaVersion,
                        JsonSerializer.SerializeToElement(new { sourceActivityExecutionId, reason = "Participant restored" }))]),
                new WorkflowAlterationOperatorProvenance(Operator, correlationId: null),
                idempotencyKey);
        }

        var jobId = WorkflowAlterationIdentity.CreateJobId(admission.Plan.PlanId, workflowExecutionId);
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
        await harness.SweepUntilQuietAsync();
        return job!;
    }
}
