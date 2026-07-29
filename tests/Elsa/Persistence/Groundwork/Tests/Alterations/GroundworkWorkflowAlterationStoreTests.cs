using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests.Alterations;

/// <summary>Provider-backed conformance coverage for the durable alteration ledger.</summary>
public sealed class GroundworkWorkflowAlterationStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Admission_replay_conflict_and_tenant_isolation_are_enforced(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var tenantA = CreateStore(fixture, "tenant-a");
        var tenantB = CreateStore(fixture, "tenant-b");

        var admitted = await tenantA.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"));
        var replay = await tenantA.AdmitAsync(Plan("other-id", "tenant-a", "key", "canonical-a"));
        var otherTenant = await tenantB.AdmitAsync(Plan("plan-b", "tenant-b", "key", "canonical-a"));

        Assert.False(admitted.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(admitted.Plan.PlanId, replay.Plan.PlanId);
        Assert.Equal("CancelWorkflow", Assert.Single(admitted.Plan.AlterationDescriptors).Kind);
        Assert.False(otherTenant.IsReplay);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.AdmitAsync(Plan("conflict", "tenant-a", "key", "canonical-b")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantB.FindPlanAsync(admitted.Plan.PlanId).AsTask());
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Concurrent_admission_materializes_tenant_idempotency_uniqueness(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");

        var results = await Task.WhenAll(
            store.AdmitAsync(Plan("plan-a", "tenant-a", "same-key", "same-canonical")).AsTask(),
            store.AdmitAsync(Plan("plan-b", "tenant-a", "same-key", "same-canonical")).AsTask());

        Assert.Single(results.Select(result => result.Plan.PlanId).Distinct(StringComparer.Ordinal));
        Assert.Single(results, result => !result.IsReplay);
        Assert.Single(results, result => result.IsReplay);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Capture_seal_and_paging_are_ordered_and_cancellation_waits_for_job_outcomes(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"))).Plan;
        await store.AdmitAsync(Plan("plan-b", "tenant-a", "key-b", "canonical-b"));
        await store.AdmitAsync(Plan("plan-c", "tenant-a", "key-c", "canonical-c"));
        var activeFirst = await store.ListActivePlansAsync(1);
        var activeSecond = await store.ListActivePlansAsync(1, activeFirst.NextCursor);
        var activeThird = await store.ListActivePlansAsync(1, activeSecond.NextCursor);

        var first = await store.CaptureAsync(plan.PlanId, plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-b", "tenant-a"), new WorkflowAlterationCapturedTarget("execution-a", "tenant-a")],
            "cursor-1");
        var finalCapture = await store.CaptureAsync(plan.PlanId, first.Revision,
            [new WorkflowAlterationCapturedTarget("execution-c", "tenant-a")],
            null);
        var cancelling = await store.RequestCancellationAsync(plan.PlanId, Now);
        var sealedPlan = await store.SealAsync(plan.PlanId, cancelling.Revision, Now);

        var firstPage = await store.PageJobsAsync(plan.PlanId, 2);
        var secondPage = await store.PageJobsAsync(plan.PlanId, 2, firstPage.NextCursor);
        var pendingCounts = await store.GetJobCountsAsync(plan.PlanId);
        var skipped = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Skipped, "PlanCancelled", null, Now);
        await store.CancelPendingJobsAsync(plan.PlanId, [skipped], Now, 2);
        var stillCancelling = await store.ReconcileAsync(plan.PlanId, Now);
        await store.CancelPendingJobsAsync(plan.PlanId, [skipped], Now, 2);
        var completed = await store.ReconcileAsync(plan.PlanId, Now);

        Assert.Null(finalCapture.CaptureCursor);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, sealedPlan.Status);
        Assert.Equal(3, sealedPlan.TargetCount);
        Assert.Equal(["execution-a", "execution-b"], firstPage.Items.Select(job => job.WorkflowExecutionId));
        Assert.Equal(["execution-c"], secondPage.Items.Select(job => job.WorkflowExecutionId));
        Assert.False(secondPage.HasNext);
        Assert.Equal("plan-a", Assert.Single(activeFirst.Items).PlanId);
        Assert.Equal("plan-b", Assert.Single(activeSecond.Items).PlanId);
        Assert.Equal("plan-c", Assert.Single(activeThird.Items).PlanId);
        Assert.Equal(3, pendingCounts.Pending);
        Assert.Equal(3, pendingCounts.Total);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, stillCancelling.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, completed.Status);
        Assert.Equal(3, completed.CancelledJobCount);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Active_plan_discovery_is_tenant_bounded_and_rejects_implicit_global_coordination(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var tenantA = CreateStore(fixture, "tenant-a");
        var tenantB = CreateStore(fixture, "tenant-b");
        var global = new GroundworkWorkflowAlterationStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            new FixedAccessContextAccessor(PersistenceAccessContext.Global),
            fixture.BoundedDocumentStore);
        var trustedCoordinator = new GroundworkWorkflowAlterationStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("runtime-alteration-coordination"))),
            fixture.BoundedDocumentStore);

        await tenantA.AdmitAsync(Plan("plan-a-2", "tenant-a", "key-a-2", "canonical-a-2"));
        await tenantB.AdmitAsync(Plan("plan-b-1", "tenant-b", "key-b-1", "canonical-b-1"));
        await tenantA.AdmitAsync(Plan("plan-a-1", "tenant-a", "key-a-1", "canonical-a-1"));

        var firstPage = await tenantA.ListActivePlansAsync(1);
        var secondPage = await tenantA.ListActivePlansAsync(1, firstPage.NextCursor);
        var tenantBPlans = await tenantB.ListActivePlansAsync(10);
        var coordinatedPlans = await trustedCoordinator.ListActivePlansAsync(10);

        Assert.Equal("plan-a-1", Assert.Single(firstPage.Items).PlanId);
        Assert.Equal("plan-a-2", Assert.Single(secondPage.Items).PlanId);
        Assert.All(firstPage.Items.Concat(secondPage.Items), plan => Assert.Equal("tenant-a", plan.AuthorityScope.TenantPartition));
        Assert.Equal("plan-b-1", Assert.Single(tenantBPlans.Items).PlanId);
        Assert.Equal(["plan-a-1", "plan-a-2", "plan-b-1"], coordinatedPlans.Items.Select(plan => plan.PlanId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.ListActivePlansAsync(10).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.RescheduleActivePlanAsync("plan-b-1", Now).AsTask());
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Active_plan_cursor_is_creation_ordered_so_new_lower_ids_are_not_starved(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        await store.AdmitAsync(Plan("plan-z", "tenant-a", "key-z", "canonical-z", Now.AddMinutes(-1)));

        _ = await store.ListActivePlansAsync(1);
        await store.AdmitAsync(Plan("plan-a", "tenant-a", "key-a", "canonical-a", Now));
        var first = await store.ListActivePlansAsync(1);
        var second = await store.ListActivePlansAsync(1, first.NextCursor);
        await store.RescheduleActivePlanAsync("plan-z", Now.AddMinutes(1));
        var leastRecentlyServiced = await store.ListActivePlansAsync(1);

        Assert.Equal("plan-z", Assert.Single(first.Items).PlanId);
        Assert.Equal("plan-a", Assert.Single(second.Items).PlanId);
        Assert.Equal("plan-a", Assert.Single(leastRecentlyServiced.Items).PlanId);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Claim_fence_expiry_and_terminal_replay_conflict_are_enforced(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"))).Plan;
        var captured = await store.CaptureAsync(plan.PlanId, plan.Revision, [new WorkflowAlterationCapturedTarget("execution-a", "tenant-a")], null);
        await store.SealAsync(plan.PlanId, captured.Revision, Now);

        var firstClaim = (await store.ClaimNextAsync(plan.PlanId, "worker-a", Now, TimeSpan.FromMinutes(1)))!;
        var running = (await store.FindPlanAsync(plan.PlanId))!;
        var expiredClaim = (await store.ClaimNextAsync(plan.PlanId, "worker-b", Now.AddMinutes(1), TimeSpan.FromMinutes(1)))!;
        var outcome = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, Now);
        var stale = new WorkflowAlterationJobTerminalChange(expiredClaim.JobId, firstClaim.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-a", Now);
        var success = new WorkflowAlterationJobTerminalChange(expiredClaim.JobId, expiredClaim.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-a", Now);

        Assert.Equal(WorkflowAlterationPlanStatus.Running, running.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyTerminalJobChangeAsync(stale).AsTask());
        await store.ApplyTerminalJobChangeAsync(success);
        await store.ApplyTerminalJobChangeAsync(success);
        var conflict = new WorkflowAlterationJobTerminalChange(expiredClaim.JobId, expiredClaim.Claim.Token, WorkflowAlterationJobStatus.Succeeded,
            [new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Different", null, Now)], "checkpoint-a", Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyTerminalJobChangeAsync(conflict).AsTask());
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Claim_reclaims_expired_running_job_after_a_bounded_terminal_prefix(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var terminalClaimTime = Now.AddMinutes(-20);
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a", terminalClaimTime))).Plan;
        var targets = Enumerable.Range(0, ElsaGroundworkQueryRoutes.MaximumResultCount + 2)
            .Select(index => new WorkflowAlterationCapturedTarget($"execution-{index:D4}", "tenant-a"))
            .ToArray();
        var captured = await store.CaptureAsync(plan.PlanId, plan.Revision, targets, null);
        await store.SealAsync(plan.PlanId, captured.Revision, terminalClaimTime);
        var outcome = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, terminalClaimTime);

        for (var index = 0; index <= ElsaGroundworkQueryRoutes.MaximumResultCount; index++)
        {
            var claim = Assert.IsType<WorkflowAlterationJobState>(
                await store.ClaimNextAsync(plan.PlanId, "terminal-worker", terminalClaimTime, TimeSpan.FromMinutes(10)));
            await store.ApplyTerminalJobChangeAsync(
                new WorkflowAlterationJobTerminalChange(
                    claim.JobId,
                    claim.Claim!.Token,
                    WorkflowAlterationJobStatus.Succeeded,
                    [outcome],
                    $"terminal-checkpoint-{index}",
                    terminalClaimTime));
        }

        var firstPage = await store.PageJobsAsync(plan.PlanId, ElsaGroundworkQueryRoutes.MaximumResultCount);
        var secondPage = await store.PageJobsAsync(plan.PlanId, ElsaGroundworkQueryRoutes.MaximumResultCount, firstPage.NextCursor);
        var expectedTarget = Assert.Single(firstPage.Items.Concat(secondPage.Items), job => job.Status == WorkflowAlterationJobStatus.Pending);
        var targetClaim = Assert.IsType<WorkflowAlterationJobState>(
            await store.ClaimNextAsync(plan.PlanId, "target-worker", terminalClaimTime.AddMinutes(1), TimeSpan.FromMinutes(10)));
        var reclaimed = Assert.IsType<WorkflowAlterationJobState>(
            await store.ClaimNextAsync(plan.PlanId, "reclaim-worker", Now, TimeSpan.FromMinutes(1)));

        Assert.Equal(expectedTarget.JobId, targetClaim.JobId);
        Assert.Equal(targetClaim.JobId, reclaimed.JobId);
        Assert.NotEqual(targetClaim.Claim!.Token, reclaimed.Claim!.Token);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Claim_redelivers_expired_running_jobs_from_cancelling_plans_before_later_work(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var oldNow = Now.AddMinutes(-5);
        var cancellingPlan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key-a", "canonical-a", oldNow))).Plan;
        var cancellingTargets = Enumerable.Range(0, ElsaGroundworkQueryRoutes.MaximumResultCount + 2)
            .Select(index => new WorkflowAlterationCapturedTarget($"execution-{index:D3}", "tenant-a"))
            .ToArray();
        var oldCapture = await store.CaptureAsync(
            cancellingPlan.PlanId,
            cancellingPlan.Revision,
            cancellingTargets,
            null);
        await store.SealAsync(cancellingPlan.PlanId, oldCapture.Revision, oldNow);
        var originalClaim = await store.ClaimNextAsync(cancellingPlan.PlanId, "old-worker", oldNow, TimeSpan.FromMinutes(1));
        await store.RequestCancellationAsync(cancellingPlan.PlanId, Now);

        var runnablePlan = (await store.AdmitAsync(Plan("plan-b", "tenant-a", "key-b", "canonical-b", Now))).Plan;
        var runnableCapture = await store.CaptureAsync(
            runnablePlan.PlanId,
            runnablePlan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-b", "tenant-a")],
            null);
        await store.SealAsync(runnablePlan.PlanId, runnableCapture.Revision, Now);

        var finishingClaim = await store.ClaimNextAsync(cancellingPlan.PlanId, "finishing-worker", Now, TimeSpan.FromMinutes(1));
        var laterClaim = await store.ClaimNextAsync(runnablePlan.PlanId, "current-worker", Now, TimeSpan.FromMinutes(1));

        Assert.NotNull(finishingClaim);
        Assert.Equal(cancellingPlan.PlanId, finishingClaim!.PlanId);
        Assert.Equal(originalClaim!.JobId, finishingClaim.JobId);
        Assert.NotNull(laterClaim);
        Assert.Equal(runnablePlan.PlanId, laterClaim!.PlanId);
        Assert.Equal("execution-b", laterClaim.WorkflowExecutionId);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Claim_is_bounded_and_progresses_after_a_cancellation_page(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var oldNow = Now.AddMinutes(-5);
        var cancellingPlan = (await store.AdmitAsync(Plan("plan-old", "tenant-a", "key-old", "canonical-old", oldNow))).Plan;
        var oldTargets = Enumerable.Range(0, ElsaGroundworkQueryRoutes.MaximumResultCount + 1)
            .Select(index => new WorkflowAlterationCapturedTarget($"old-execution-{index:D3}", "tenant-a"))
            .ToArray();
        var oldCapture = await store.CaptureAsync(cancellingPlan.PlanId, cancellingPlan.Revision, oldTargets, null);
        await store.SealAsync(cancellingPlan.PlanId, oldCapture.Revision, oldNow);
        await store.RequestCancellationAsync(cancellingPlan.PlanId, Now);

        var runnablePlan = (await store.AdmitAsync(Plan("plan-runnable", "tenant-a", "key-runnable", "canonical-runnable", Now))).Plan;
        var runnableCapture = await store.CaptureAsync(
            runnablePlan.PlanId,
            runnablePlan.Revision,
            [new WorkflowAlterationCapturedTarget("runnable-execution", "tenant-a")],
            null);
        await store.SealAsync(runnablePlan.PlanId, runnableCapture.Revision, Now);

        var boundedClaim = await store.ClaimNextAsync(cancellingPlan.PlanId, "worker", Now, TimeSpan.FromMinutes(1));
        var skipped = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Skipped, "PlanCancelled", null, Now);
        await store.CancelPendingJobsAsync(cancellingPlan.PlanId, [skipped], Now, ElsaGroundworkQueryRoutes.MaximumResultCount);
        var progressedClaim = await store.ClaimNextAsync(runnablePlan.PlanId, "worker", Now, TimeSpan.FromMinutes(1));

        Assert.Null(boundedClaim);
        Assert.NotNull(progressedClaim);
        Assert.Equal(runnablePlan.PlanId, progressedClaim!.PlanId);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Plan_scoped_claim_is_not_blocked_by_an_older_capturing_plan(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var capturing = (await store.AdmitAsync(Plan("capturing", "tenant-a", "capturing-key", "capturing-canonical", Now.AddMinutes(-1)))).Plan;
        var provisionalTargets = Enumerable.Range(0, ElsaGroundworkQueryRoutes.MaximumResultCount + 1)
            .Select(index => new WorkflowAlterationCapturedTarget($"provisional-{index:D3}", "tenant-a"))
            .ToArray();
        await store.CaptureAsync(capturing.PlanId, capturing.Revision, provisionalTargets, "more");

        var runnable = (await store.AdmitAsync(Plan("runnable", "tenant-a", "runnable-key", "runnable-canonical", Now))).Plan;
        var captured = await store.CaptureAsync(runnable.PlanId, runnable.Revision, [new WorkflowAlterationCapturedTarget("runnable-execution", "tenant-a")], null);
        await store.SealAsync(runnable.PlanId, captured.Revision, Now);

        var claimed = await store.ClaimNextAsync(runnable.PlanId, "worker", Now, TimeSpan.FromMinutes(1));

        Assert.NotNull(claimed);
        Assert.Equal(runnable.PlanId, claimed!.PlanId);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Unsealed_capture_terminalization_discards_provisional_jobs(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var cancelledPlan = (await store.AdmitAsync(Plan("cancel", "tenant-a", "cancel-key", "cancel-canonical"))).Plan;
        var failedPlan = (await store.AdmitAsync(Plan("fail", "tenant-a", "fail-key", "fail-canonical"))).Plan;
        var provisionalTargets = Enumerable.Range(0, 101)
            .Select(index => new WorkflowAlterationCapturedTarget($"execution-{index:D3}", "tenant-a"))
            .ToArray();
        await store.CaptureAsync(cancelledPlan.PlanId, cancelledPlan.Revision, provisionalTargets, "cursor");
        await store.CaptureAsync(failedPlan.PlanId, failedPlan.Revision, provisionalTargets, "cursor");

        var cancelling = await store.CancelUnsealedCaptureAsync(cancelledPlan.PlanId, Now);
        var failing = await store.FailUnsealedCaptureAsync(failedPlan.PlanId, new WorkflowAlterationSafeFailure("CaptureRetryExhausted"), Now);
        var resumed = CreateStore(fixture, "tenant-a");
        var cancelled = await resumed.FailUnsealedCaptureAsync(cancelledPlan.PlanId, new WorkflowAlterationSafeFailure("LaterFailure"), Now.AddMinutes(1));
        var failed = await resumed.CancelUnsealedCaptureAsync(failedPlan.PlanId, Now.AddMinutes(1));

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, cancelling.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, failing.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, cancelled.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Failed, failed.Status);
        Assert.Equal("CaptureRetryExhausted", failed.SafeFailure?.Code);
        Assert.Null(cancelled.SealedAt);
        Assert.Null(failed.SealedAt);
        Assert.Equal(101, cancelled.CapturedSoFar);
        Assert.Equal(101, failed.CapturedSoFar);
        Assert.Equal(0, cancelled.TargetCount);
        Assert.Equal(0, failed.TargetCount);
        Assert.Empty((await resumed.PageJobsAsync(cancelled.PlanId, 10)).Items);
        Assert.Empty((await resumed.PageJobsAsync(failed.PlanId, 10)).Items);
        Assert.Equal(0, (await resumed.GetJobCountsAsync(cancelled.PlanId)).Total);
        Assert.Equal(0, (await resumed.GetJobCountsAsync(failed.PlanId)).Total);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Capture_revision_conflicts_use_the_shared_retry_signal(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var plan = (await store.AdmitAsync(Plan("concurrency", "tenant-a", "key", "canonical"))).Plan;
        await store.CaptureAsync(
            plan.PlanId,
            plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-a", "tenant-a")],
            "cursor");

        await Assert.ThrowsAsync<WorkflowAlterationConcurrencyException>(() => store.CaptureAsync(
            plan.PlanId,
            plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-b", "tenant-a")],
            null).AsTask());
    }

    private static GroundworkWorkflowAlterationStore CreateStore(GroundworkDocumentStoreFixture fixture, string tenant) => new(
        fixture.DocumentStore,
        GroundworkTestSerialization.Serializer,
        GroundworkTestAccess.AccessContext(tenant),
        fixture.BoundedDocumentStore);

    private static WorkflowAlterationPlanState Plan(
        string id,
        string tenant,
        string key,
        string canonical,
        DateTimeOffset? createdAt = null) =>
        WorkflowAlterationPlanState.CreateCapturing(
            id,
            new WorkflowAlterationAuthorityScope(tenant, "system", "operator"),
            new WorkflowAlterationOperatorProvenance("operator", "correlation"),
            key,
            canonical,
            new ProtectedWorkflowAlterationPayload("development", "A256GCM", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["example"]),
            createdAt ?? Now,
            [new WorkflowAlterationDescriptor("CancelWorkflow", 1, "Cancel workflow")]);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
