using Elsa.Persistence.Groundwork.Stores;
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
    public async Task Capture_seal_and_paging_are_ordered_and_cancellation_waits_for_job_outcomes(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var plan = (await store.AdmitAsync(Plan("plan-a", "tenant-a", "key", "canonical-a"))).Plan;
        await store.AdmitAsync(Plan("plan-b", "tenant-a", "key-b", "canonical-b"));
        var activeFirst = await store.ListActivePlansAsync(1);
        var activeSecond = await store.ListActivePlansAsync(1, activeFirst.NextCursor);

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
        await store.CancelPendingJobsAsync(plan.PlanId, [skipped], Now);
        var completed = await store.ReconcileAsync(plan.PlanId, Now);

        Assert.Null(finalCapture.CaptureCursor);
        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, sealedPlan.Status);
        Assert.Equal(3, sealedPlan.TargetCount);
        Assert.Equal(["execution-a", "execution-b"], firstPage.Items.Select(job => job.WorkflowExecutionId));
        Assert.Equal(["execution-c"], secondPage.Items.Select(job => job.WorkflowExecutionId));
        Assert.False(secondPage.HasNext);
        Assert.Equal("plan-a", Assert.Single(activeFirst.Items).PlanId);
        Assert.Equal("plan-b", Assert.Single(activeSecond.Items).PlanId);
        Assert.Equal(3, pendingCounts.Pending);
        Assert.Equal(3, pendingCounts.Total);
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

        var firstClaim = (await store.ClaimNextAsync("worker-a", Now, TimeSpan.FromMinutes(1)))!;
        var running = (await store.FindPlanAsync(plan.PlanId))!;
        var expiredClaim = (await store.ClaimNextAsync("worker-b", Now.AddMinutes(1), TimeSpan.FromMinutes(1)))!;
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
    public async Task Unsealed_capture_terminalization_discards_provisional_jobs(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = CreateStore(fixture, "tenant-a");
        var cancelledPlan = (await store.AdmitAsync(Plan("cancel", "tenant-a", "cancel-key", "cancel-canonical"))).Plan;
        var failedPlan = (await store.AdmitAsync(Plan("fail", "tenant-a", "fail-key", "fail-canonical"))).Plan;
        await store.CaptureAsync(cancelledPlan.PlanId, cancelledPlan.Revision, [new WorkflowAlterationCapturedTarget("execution-cancel", "tenant-a")], "cursor");
        await store.CaptureAsync(failedPlan.PlanId, failedPlan.Revision, [new WorkflowAlterationCapturedTarget("execution-fail", "tenant-a")], "cursor");

        var cancelled = await store.CancelUnsealedCaptureAsync(cancelledPlan.PlanId, Now);
        var failed = await store.FailUnsealedCaptureAsync(failedPlan.PlanId, new WorkflowAlterationSafeFailure("CaptureRetryExhausted"), Now);

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, cancelled.Status);
        Assert.Equal(WorkflowAlterationPlanStatus.Failed, failed.Status);
        Assert.Null(cancelled.SealedAt);
        Assert.Null(failed.SealedAt);
        Assert.Equal(1, cancelled.CapturedSoFar);
        Assert.Equal(1, failed.CapturedSoFar);
        Assert.Equal(0, cancelled.TargetCount);
        Assert.Equal(0, failed.TargetCount);
        Assert.Empty((await store.PageJobsAsync(cancelled.PlanId, 10)).Items);
        Assert.Empty((await store.PageJobsAsync(failed.PlanId, 10)).Items);
        Assert.Equal(0, (await store.GetJobCountsAsync(cancelled.PlanId)).Total);
        Assert.Equal(0, (await store.GetJobCountsAsync(failed.PlanId)).Total);
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

    private static WorkflowAlterationPlanState Plan(string id, string tenant, string key, string canonical) =>
        WorkflowAlterationPlanState.CreateCapturing(
            id,
            new WorkflowAlterationAuthorityScope(tenant, "system", "operator"),
            new WorkflowAlterationOperatorProvenance("operator", "correlation"),
            key,
            canonical,
            new ProtectedWorkflowAlterationPayload("development", "A256GCM", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["example"]),
            Now,
            [new WorkflowAlterationDescriptor("CancelWorkflow", 1, "Cancel workflow")]);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
