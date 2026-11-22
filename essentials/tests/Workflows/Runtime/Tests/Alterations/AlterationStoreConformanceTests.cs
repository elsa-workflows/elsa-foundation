using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class AlterationStoreConformanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Admission_IsTenantScopedAndReturnsReplayForTheSameCanonicalRequest()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var admitted = await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"));
        var replay = await store.AdmitAsync(Plan("ignored", "tenant-a", "key", "canonical-a"));
        var otherTenant = await store.AdmitAsync(Plan("plan-b", "tenant-b", "key", "canonical-a"));

        Assert.False(admitted.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal("plan-a", replay.Plan.PlanId);
        Assert.False(otherTenant.IsReplay);
        await Assert.ThrowsAsync<WorkflowAlterationIdempotencyConflictException>(() => store.AdmitAsync(Plan("conflict", "tenant-a", "key", "canonical-b")).AsTask());
    }

    [Fact]
    public async Task Capture_DeduplicatesInImmutableExecutionIdOrder_AndDoesNotClaimBeforeSeal()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"))).Plan;
        var captured = await store.CaptureAsync(plan.PlanId, plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-b", "tenant-a"), new WorkflowAlterationCapturedTarget("execution-a", "tenant-a"), new WorkflowAlterationCapturedTarget("execution-b", "tenant-a")],
            "cursor-1");

        Assert.Null(await store.ClaimNextAsync(plan.PlanId, "worker", Now, TimeSpan.FromMinutes(1)));
        var finalCapture = await store.CaptureAsync(plan.PlanId, captured.Revision, [], null);
        var sealedPlan = await store.SealAsync(plan.PlanId, finalCapture.Revision, Now);
        var jobs = await store.PageJobsAsync(plan.PlanId, 10);

        Assert.Null(finalCapture.CaptureCursor);
        Assert.Equal(WorkflowAlterationPlanStatus.Queued, sealedPlan.Status);
        Assert.Equal(["execution-a", "execution-b"], jobs.Items.Select(job => job.WorkflowExecutionId));
        Assert.NotNull(await store.ClaimNextAsync(plan.PlanId, "worker", Now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Cancellation_RequiresCallerSuppliedSafeSkippedEvidenceAndReconcilesOnlyAfterSeal()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"))).Plan;
        var captured = await store.CaptureAsync(plan.PlanId, plan.Revision, [new WorkflowAlterationCapturedTarget("execution-a", "tenant-a")], null);
        var cancelling = await store.RequestCancellationAsync(plan.PlanId, Now);
        var sealedPlan = await store.SealAsync(plan.PlanId, cancelling.Revision, Now);
        var skipped = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Skipped, "PlanCancelled", null, Now);

        await store.CancelPendingJobsAsync(plan.PlanId, [skipped], Now, 100);
        var completed = await store.ReconcileAsync(plan.PlanId, Now);
        var job = Assert.Single((await store.PageJobsAsync(plan.PlanId, 10)).Items);

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, sealedPlan.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, completed.Status);
        Assert.Equal(WorkflowAlterationJobStatus.Cancelled, job.Status);
        Assert.Equal("PlanCancelled", Assert.Single(job.Outcomes).Code);
    }

    [Fact]
    public async Task ExplicitMissingTarget_IsDurableFailedJobEvidence()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"))).Plan;
        var captured = await store.CaptureAsync(plan.PlanId, plan.Revision,
            [new WorkflowAlterationCapturedTarget("not-found", "tenant-a", safeFailure: new WorkflowAlterationSafeFailure("TargetNotFound"))], null);
        await store.SealAsync(plan.PlanId, captured.Revision, Now);
        var job = Assert.Single((await store.PageJobsAsync(plan.PlanId, 10)).Items);

        Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
        Assert.Equal("TargetNotFound", job.SafeFailure?.Code);
        Assert.Null(await store.ClaimNextAsync(plan.PlanId, "worker", Now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Claim_reclaims_an_expired_running_job_before_an_older_pending_cohort()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"))).Plan;
        var targets = Enumerable.Range(0, 101)
            .Select(index => new WorkflowAlterationCapturedTarget($"execution-{index:D3}", "tenant-a"))
            .ToArray();
        var captured = await store.CaptureAsync(plan.PlanId, plan.Revision, targets, null);
        await store.SealAsync(plan.PlanId, captured.Revision, Now);

        var original = Assert.IsType<WorkflowAlterationJobState>(
            await store.ClaimNextAsync(plan.PlanId, "first-worker", Now, TimeSpan.FromMinutes(1)));
        var recovered = Assert.IsType<WorkflowAlterationJobState>(
            await store.ClaimNextAsync(plan.PlanId, "recovery-worker", Now.AddMinutes(2), TimeSpan.FromMinutes(1)));

        Assert.Equal(original.JobId, recovered.JobId);
        Assert.NotEqual(original.Claim!.Token, recovered.Claim!.Token);
        Assert.Equal(2, recovered.AttemptCount);
    }

    [Fact]
    public async Task Unsealed_cleanup_is_page_bounded_restartable_and_preserves_the_first_terminal_intent()
    {
        var state = new InMemoryWorkflowAlterationStoreState();
        var store = new InMemoryWorkflowAlterationStore(state);
        var cancelledPlan = (await store.AdmitAsync(Plan("cancel", "tenant-a", "cancel-key", "cancel-canonical"))).Plan;
        var failedPlan = (await store.AdmitAsync(Plan("fail", "tenant-a", "fail-key", "fail-canonical"))).Plan;
        var targets = Enumerable.Range(0, 101)
            .Select(index => new WorkflowAlterationCapturedTarget($"execution-{index:D3}", "tenant-a"))
            .ToArray();
        await store.CaptureAsync(cancelledPlan.PlanId, cancelledPlan.Revision, targets, "cursor");
        await store.CaptureAsync(failedPlan.PlanId, failedPlan.Revision, targets, "cursor");

        var cancelling = await store.CancelUnsealedCaptureAsync(cancelledPlan.PlanId, Now);
        var failing = await store.FailUnsealedCaptureAsync(
            failedPlan.PlanId,
            new WorkflowAlterationSafeFailure("CaptureRetryExhausted"),
            Now);
        var resumed = new InMemoryWorkflowAlterationStore(state);
        var cancelled = await resumed.FailUnsealedCaptureAsync(
            cancelledPlan.PlanId,
            new WorkflowAlterationSafeFailure("LaterFailure"),
            Now.AddMinutes(1));
        var failed = await resumed.CancelUnsealedCaptureAsync(failedPlan.PlanId, Now.AddMinutes(1));

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, cancelling.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, failing.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, cancelled.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Failed, failed.Status);
        Assert.Equal("CaptureRetryExhausted", failed.SafeFailure?.Code);
        Assert.Empty((await resumed.PageJobsAsync(cancelled.PlanId, 10)).Items);
        Assert.Empty((await resumed.PageJobsAsync(failed.PlanId, 10)).Items);
    }

    [Fact]
    public async Task TerminalReplay_RequiresEquivalentClaimFencedEvidence()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"))).Plan;
        var captured = await store.CaptureAsync(plan.PlanId, plan.Revision, [new WorkflowAlterationCapturedTarget("execution-a", "tenant-a")], null);
        await store.SealAsync(plan.PlanId, captured.Revision, Now);
        var claimed = (await store.ClaimNextAsync(plan.PlanId, "worker", Now, TimeSpan.FromMinutes(1)))!;
        var success = new WorkflowAlterationJobTerminalChange(claimed.JobId, claimed.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, Now)], "checkpoint-1", Now);

        await store.ApplyTerminalJobChangeAsync(success);
        await store.ApplyTerminalJobChangeAsync(success);
        var conflict = new WorkflowAlterationJobTerminalChange(claimed.JobId, claimed.Claim.Token, WorkflowAlterationJobStatus.Succeeded, [new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Different", null, Now)], "checkpoint-1", Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyTerminalJobChangeAsync(conflict).AsTask());
    }

    private static WorkflowAlterationPlanState Plan(string id, string tenant, string key, string canonical) =>
        WorkflowAlterationPlanState.CreateCapturing(
            id,
            new WorkflowAlterationAuthorityScope(tenant, "system", "operator"),
            new WorkflowAlterationOperatorProvenance("operator", "correlation"),
            key,
            canonical,
            new ProtectedWorkflowAlterationPayload("development", "A256GCM", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["example"]),
            Now);
}
