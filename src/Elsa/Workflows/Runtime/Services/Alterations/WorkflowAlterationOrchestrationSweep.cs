using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Executes one bounded, durable orchestration sweep. It discovers non-terminal plans from the store on every tick so
/// a host restart resumes capture, cancellation, dispatch, and reconciliation without relying on submission callbacks.
/// </summary>
public sealed class WorkflowAlterationOrchestrationSweep(
    IWorkflowAlterationStore store,
    WorkflowAlterationTargetCaptureTask captureTask,
    WorkflowAlterationJobTask jobTask,
    WorkflowAlterationPlanReconciliationTask reconciliationTask,
    WorkflowAlterationPlanService planService)
{
    private readonly IWorkflowAlterationStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly WorkflowAlterationTargetCaptureTask _captureTask = captureTask ?? throw new ArgumentNullException(nameof(captureTask));
    private readonly WorkflowAlterationJobTask _jobTask = jobTask ?? throw new ArgumentNullException(nameof(jobTask));
    private readonly WorkflowAlterationPlanReconciliationTask _reconciliationTask = reconciliationTask ?? throw new ArgumentNullException(nameof(reconciliationTask));
    private readonly WorkflowAlterationPlanService _planService = planService ?? throw new ArgumentNullException(nameof(planService));

    public async ValueTask<WorkflowAlterationOrchestrationSweepResult> ExecuteAsync(
        WorkflowAlterationOrchestrationOptions options,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        // One page only: a perpetually busy tenant cannot turn one scheduled tick into an unbounded foreground loop.
        var page = await _store.ListActivePlansAsync(options.MaxPlansPerSweep, cancellationToken: cancellationToken);
        var dispatched = 0;
        foreach (var discovered in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = discovered;
            if (plan.Status == WorkflowAlterationPlanStatus.CapturingTargets)
                plan = (await _captureTask.CaptureNextAsync(plan.PlanId, options.CapturePageSize, cancellationToken)) ?? plan;
            else if (plan.Status == WorkflowAlterationPlanStatus.Cancelling)
                // CancelAsync is idempotent: an unsealed capture is terminalized without retaining jobs; a sealed
                // cohort cancels only pending work and leaves a running actor job to finish its checkpoint.
                plan = await _planService.CancelAsync(plan.PlanId, cancellationToken);

            plan = await _store.FindPlanAsync(plan.PlanId, cancellationToken) ?? plan;
            if ((plan.Status is WorkflowAlterationPlanStatus.Queued or WorkflowAlterationPlanStatus.Running) && dispatched < options.MaxJobClaimsPerSweep)
            {
                var remaining = options.MaxJobClaimsPerSweep - dispatched;
                dispatched += (await _jobTask.DispatchAvailableAsync(workerId, remaining, options.JobLeaseDuration, cancellationToken)).Count;
            }

            await _reconciliationTask.ReconcileAsync(plan.PlanId, cancellationToken);
        }

        return new WorkflowAlterationOrchestrationSweepResult(page.Items.Count, dispatched, page.HasNext);
    }
}

/// <summary>Bounded work performed by one orchestration tick.</summary>
public sealed record WorkflowAlterationOrchestrationSweepResult(int PlanCount, int DispatchCount, bool HasMorePlans);
