using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class WorkflowAlterationOrchestrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Submission_ReplaysTheSameCanonicalRequestAndRejectsIdempotencyConflict()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var service = NewPlanService(store);
        var submission = NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"]));

        var first = await service.SubmitAsync(submission, new WorkflowAlterationOperatorProvenance("operator", null), "key-1");
        var replay = await service.SubmitAsync(submission, new WorkflowAlterationOperatorProvenance("operator", null), "key-1");

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Plan.PlanId, replay.Plan.PlanId);
        await Assert.ThrowsAsync<WorkflowAlterationIdempotencyConflictException>(async () =>
            await service.SubmitAsync(NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-2"])), new WorkflowAlterationOperatorProvenance("operator", null), "key-1"));
    }

    [Fact]
    public async Task Capture_UsesBoundedImmutablePagesSealsTheCohortAndPersistsConcurrencyFacts()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(NewExecution("execution-c"));
        await executions.SaveAsync(NewExecution("execution-a"));
        await executions.SaveAsync(NewExecution("execution-b"));
        var service = NewPlanService(store);
        var admission = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForQuery(new WorkflowAlterationQuerySelector(matchAllAuthorized: true))),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "key-1");
        var capture = new WorkflowAlterationTargetCaptureTask(store, executions, new FixedTimeProvider(Now));

        await capture.CaptureNextAsync(admission.Plan.PlanId, 1);
        await capture.CaptureNextAsync(admission.Plan.PlanId, 1);
        var sealedPlan = await capture.CaptureNextAsync(admission.Plan.PlanId, 1);
        var jobs = await store.PageJobsAsync(admission.Plan.PlanId, 10);

        Assert.Equal(WorkflowAlterationPlanStatus.Queued, sealedPlan!.Status);
        Assert.Equal(["execution-a", "execution-b", "execution-c"], jobs.Items.Select(job => job.WorkflowExecutionId));
        Assert.All(jobs.Items, job => Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(42).UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), job.CapturedConcurrency!.WorkflowStateRevision));
        Assert.All(jobs.Items, job => Assert.Equal(7, job.CapturedConcurrency!.RootVariableFrameRevision));
    }

    [Fact]
    public async Task ExplicitCapture_NormalizesTheDefaultTenantPartition()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(NewExecution("execution-default") with { TenantId = null });
        var service = NewPlanService(store);
        var submission = new WorkflowAlterationSubmission(
            new WorkflowAlterationAuthorityScope(WorkflowExecutionPartition.DefaultValue, "system", "operator"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["execution-default"]),
            [new WorkflowAlterationEnvelope("CancelWorkflow", 1, JsonSerializer.SerializeToElement(new { }))]);
        var admission = await service.SubmitAsync(
            submission,
            new WorkflowAlterationOperatorProvenance("operator", null),
            "key-default");

        var sealedPlan = await new WorkflowAlterationTargetCaptureTask(store, executions, new FixedTimeProvider(Now))
            .CaptureNextAsync(admission.Plan.PlanId, 1);
        var job = Assert.Single((await store.PageJobsAsync(admission.Plan.PlanId, 10)).Items);

        Assert.Equal(WorkflowAlterationPlanStatus.Queued, sealedPlan!.Status);
        Assert.Equal(WorkflowAlterationJobStatus.Pending, job.Status);
        Assert.Equal(WorkflowExecutionPartition.DefaultValue, job.TenantPartition);
        Assert.Null(job.SafeFailure);
    }

    [Fact]
    public async Task Capture_IsolatesTargetsByExecutionAuthorityWithinTheSameTenant()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(NewExecution("execution-authorized"));
        await executions.SaveAsync(NewExecution("execution-other") with
        {
            Authority = new WorkflowExecutionAuthoritySnapshot("system", "other-root")
        });
        var service = NewPlanService(store);
        var queryPlan = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForQuery(new WorkflowAlterationQuerySelector(matchAllAuthorized: true))),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "authority-query");
        var explicitPlan = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-other"])),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "authority-explicit");
        var capture = new WorkflowAlterationTargetCaptureTask(store, executions, new FixedTimeProvider(Now));

        await capture.CaptureNextAsync(queryPlan.Plan.PlanId, 10);
        await capture.CaptureNextAsync(explicitPlan.Plan.PlanId, 10);

        var queryJob = Assert.Single((await store.PageJobsAsync(queryPlan.Plan.PlanId, 10)).Items);
        var inaccessibleJob = Assert.Single((await store.PageJobsAsync(explicitPlan.Plan.PlanId, 10)).Items);
        Assert.Equal("execution-authorized", queryJob.WorkflowExecutionId);
        Assert.Equal("operator", queryJob.CapturedConcurrency!.Authority!.RootInitiator);
        Assert.Equal(WorkflowAlterationJobStatus.Failed, inaccessibleJob.Status);
        Assert.Equal("TargetNotFound", inaccessibleJob.SafeFailure!.Code);
    }

    [Fact]
    public async Task Capture_IsolatesTargetsByAuthorityMetadataWithinTheSameTenant()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        var approved = new Dictionary<string, string> { ["permission"] = "approved" };
        await executions.SaveAsync(NewExecution("execution-approved") with
        {
            Authority = new WorkflowExecutionAuthoritySnapshot("system", "operator", approved)
        });
        await executions.SaveAsync(NewExecution("execution-other") with
        {
            Authority = new WorkflowExecutionAuthoritySnapshot(
                "system",
                "operator",
                new Dictionary<string, string> { ["permission"] = "other" })
        });
        var service = NewPlanService(store);
        var submission = new WorkflowAlterationSubmission(
            new WorkflowAlterationAuthorityScope("tenant-a", "system", "operator", approved),
            WorkflowAlterationTargetSelector.ForQuery(new WorkflowAlterationQuerySelector(matchAllAuthorized: true)),
            [new WorkflowAlterationEnvelope("CancelWorkflow", 1, JsonSerializer.SerializeToElement(new { }))]);
        var plan = await service.SubmitAsync(
            submission,
            new WorkflowAlterationOperatorProvenance("operator", null),
            "authority-metadata-query");

        await new WorkflowAlterationTargetCaptureTask(store, executions, new FixedTimeProvider(Now))
            .CaptureNextAsync(plan.Plan.PlanId, 10);

        var job = Assert.Single((await store.PageJobsAsync(plan.Plan.PlanId, 10)).Items);
        Assert.Equal("execution-approved", job.WorkflowExecutionId);
        Assert.Equal("approved", job.CapturedConcurrency!.Authority!.Metadata["permission"]);
    }

    [Fact]
    public async Task Cancellation_StopsPendingJobsWithSafeSkippedOutcomesAndReconcilesTerminalCounts()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(NewExecution("execution-1"));
        var service = NewPlanService(store);
        var admission = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"])),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "key-1");
        var capture = new WorkflowAlterationTargetCaptureTask(store, executions, new FixedTimeProvider(Now));
        await capture.CaptureNextAsync(admission.Plan.PlanId, 10);

        var cancelled = await service.CancelAsync(admission.Plan.PlanId);
        var job = Assert.Single((await store.PageJobsAsync(admission.Plan.PlanId, 10)).Items);

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.CancelledJobCount);
        Assert.Equal(WorkflowAlterationJobStatus.Cancelled, job.Status);
        var outcome = Assert.Single(job.Outcomes);
        Assert.Equal(WorkflowAlterationOutcomeStatus.Skipped, outcome.Status);
        Assert.Equal("PlanCancelled", outcome.Code);
    }

    [Fact]
    public async Task Cancellation_UsesPersistedDescriptorsWhenProtectedPayloadCannotBeRead()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var service = NewPlanService(store);
        var admission = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"])),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "key-with-unavailable-payload");
        var captured = await store.CaptureAsync(
            admission.Plan.PlanId,
            admission.Plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")],
            null);
        await store.SealAsync(captured.PlanId, captured.Revision, Now);
        var cancellationService = NewPlanService(store, new ThrowingUnprotectPayloadProtector());

        var cancelled = await cancellationService.CancelAsync(admission.Plan.PlanId);
        var outcome = Assert.Single(Assert.Single((await store.PageJobsAsync(admission.Plan.PlanId, 10)).Items).Outcomes);

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, cancelled.Status);
        Assert.Equal("CancelWorkflow", outcome.Kind);
        Assert.Equal(1, outcome.SchemaVersion);
        Assert.Equal("PlanCancelled", outcome.Code);
    }

    [Fact]
    public async Task Cancellation_RedeliversAnExpiredRunningJobSoItsAtomicCheckpointCanFinish()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var service = NewPlanService(store);
        var admission = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"])),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "cancelling-running-job");
        var captured = await store.CaptureAsync(
            admission.Plan.PlanId,
            admission.Plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")],
            null);
        await store.SealAsync(captured.PlanId, captured.Revision, Now);
        var originalClaim = await store.ClaimNextAsync(admission.Plan.PlanId, "worker-1", Now, TimeSpan.FromMinutes(1));

        var cancelling = await service.CancelAsync(admission.Plan.PlanId);
        var finishingClaim = await store.ClaimNextAsync(admission.Plan.PlanId, "worker-2", Now.AddMinutes(1), TimeSpan.FromMinutes(1));

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelling, cancelling.Status);
        Assert.NotNull(originalClaim);
        Assert.NotNull(finishingClaim);
        Assert.Equal(originalClaim!.JobId, finishingClaim!.JobId);
        Assert.NotEqual(originalClaim.Claim!.Token, finishingClaim.Claim!.Token);
        Assert.Equal(WorkflowAlterationJobStatus.Running, finishingClaim.Status);
    }

    [Fact]
    public async Task Cancellation_DuringUnsealedCapture_DiscardsProvisionalJobsWithoutSealing()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var service = NewPlanService(store);
        var admission = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1", "execution-2"])),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "capture-cancel");
        await store.CaptureAsync(admission.Plan.PlanId, admission.Plan.Revision, [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], "explicit:1");

        var cancelled = await service.CancelAsync(admission.Plan.PlanId);
        var jobs = await store.PageJobsAsync(admission.Plan.PlanId, 10);
        var counts = await store.GetJobCountsAsync(admission.Plan.PlanId);

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.SealedAt);
        Assert.Equal(1, cancelled.CapturedSoFar);
        Assert.Equal(0, cancelled.TargetCount);
        Assert.Empty(jobs.Items);
        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public async Task Cancellation_ThatRacesWithSealing_ContinuesAcrossTheSealedCohort()
    {
        var inner = new InMemoryWorkflowAlterationStore();
        var store = new SealBeforeUnsealedCancellationStore(inner, Now);
        var service = NewPlanService(store);
        var admission = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"])),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "seal-cancel-race");
        await store.CaptureAsync(
            admission.Plan.PlanId,
            admission.Plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")],
            null);

        var cancelled = await service.CancelAsync(admission.Plan.PlanId);
        var job = Assert.Single((await store.PageJobsAsync(admission.Plan.PlanId, 10)).Items);

        Assert.Equal(WorkflowAlterationPlanStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.SealedAt);
        Assert.Equal(WorkflowAlterationJobStatus.Cancelled, job.Status);
    }

    [Fact]
    public async Task CaptureRetryExhaustion_FailsUnsealedPlanAndDiscardsProvisionalJobs()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        var service = NewPlanService(store);
        var admission = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1", "execution-2"])),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "capture-failure");
        await store.CaptureAsync(admission.Plan.PlanId, admission.Plan.Revision, [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a")], "explicit:1");
        var capture = new WorkflowAlterationTargetCaptureTask(
            store,
            executions,
            new FixedTimeProvider(Now),
            new WorkflowAlterationTargetCaptureOptions { MaxConcurrencyRetries = 0 });

        var failed = await capture.CaptureNextAsync(admission.Plan.PlanId, 1);
        var jobs = await store.PageJobsAsync(admission.Plan.PlanId, 10);
        var counts = await store.GetJobCountsAsync(admission.Plan.PlanId);

        Assert.Equal(WorkflowAlterationPlanStatus.Failed, failed!.Status);
        Assert.Equal("CaptureRetryExhausted", failed.SafeFailure!.Code);
        Assert.Null(failed.SealedAt);
        Assert.Equal(1, failed.CapturedSoFar);
        Assert.Equal(0, failed.TargetCount);
        Assert.Empty(jobs.Items);
        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public async Task Leasing_IsBoundedAndExpiredClaimsAreRedelivered()
    {
        var store = new InMemoryWorkflowAlterationStore();
        var service = NewPlanService(store);
        var admission = await service.SubmitAsync(
            NewSubmission(WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1", "execution-2"])),
            new WorkflowAlterationOperatorProvenance("operator", null),
            "key-1");
        var plan = admission.Plan;
        plan = await store.CaptureAsync(plan.PlanId, plan.Revision,
            [new WorkflowAlterationCapturedTarget("execution-1", "tenant-a"), new WorkflowAlterationCapturedTarget("execution-2", "tenant-a")],
            null);
        await store.SealAsync(plan.PlanId, plan.Revision, Now);

        var first = await store.ClaimNextAsync(admission.Plan.PlanId, "worker-1", Now, TimeSpan.FromSeconds(1));
        var second = await store.ClaimNextAsync(admission.Plan.PlanId, "worker-2", Now, TimeSpan.FromSeconds(1));
        var redelivery = await store.ClaimNextAsync(admission.Plan.PlanId, "worker-3", Now.AddSeconds(2), TimeSpan.FromSeconds(1));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(redelivery);
        Assert.Equal(first!.JobId, redelivery!.JobId);
        Assert.Equal(2, redelivery.AttemptCount);
    }

    [Fact]
    public async Task ActorDispatcher_UsesDeterministicAtLeastOnceAlterWorkflowCommandWithoutPayloads()
    {
        var provider = new RecordingActorProvider();
        var dispatcher = new WorkflowAlterationJobDispatcher(provider, new FixedTimeProvider(Now));
        var job = NewRunningJob("job-1", "execution-1");

        await dispatcher.DispatchAsync(job);
        await dispatcher.DispatchAsync(job);

        var first = provider.Actor.Envelopes[0];
        var second = provider.Actor.Envelopes[1];
        Assert.Equal(WorkflowExecutionCommandKind.AlterWorkflow, first.Command.Kind);
        Assert.Equal(first.EnvelopeId, second.EnvelopeId);
        Assert.Equal(first.Command.CommandId, second.Command.CommandId);
        Assert.Equal(WorkflowExecutionCommandDeliveryMode.AtLeastOnce, first.DeliveryMode);
        var payload = first.Command.Payload!.Value.Deserialize<WorkflowAlterationActorCommand>();
        Assert.Equal(job.JobId, payload!.JobId);
        Assert.Equal(job.Claim!.Token, payload.ClaimToken);
        Assert.DoesNotContain("payload", first.Command.Payload!.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static WorkflowAlterationPlanService NewPlanService(
        IWorkflowAlterationStore store,
        IWorkflowAlterationPayloadProtector? payloadProtector = null) =>
        new(
            new WorkflowAlterationRegistry(
            [
                new WorkflowAlterationHandlerContribution(
                    new WorkflowAlterationDescriptor("CancelWorkflow", 1, "Cancel workflow"),
                    typeof(CancelWorkflowAlterationHandler))
            ]),
            payloadProtector ?? new TestPayloadProtector(),
            store,
            new FixedTimeProvider(Now));

    private static WorkflowAlterationSubmission NewSubmission(WorkflowAlterationTargetSelector target) =>
        new(
            new WorkflowAlterationAuthorityScope("tenant-a", "system", "operator"),
            target,
            [new WorkflowAlterationEnvelope("CancelWorkflow", 1, JsonSerializer.SerializeToElement(new { }))]);

    private static WorkflowExecutionState NewExecution(string id) =>
        new(
            id,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:1"),
            WorkflowExecutionStatus.Running,
            null,
            Now,
            Now,
            DateTimeOffset.FromUnixTimeSeconds(42),
            null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>())
        {
            Authority = new WorkflowExecutionAuthoritySnapshot("system", "operator"),
            RootVariableFrame = new VariableFrameState("frame-1", "scope-1", "activation-1", null, VariableFrameKind.Root, new Dictionary<string, ValueEnvelope>(), 7)
        };

    private static WorkflowAlterationJobState NewRunningJob(string jobId, string executionId) =>
        new(jobId, "plan-1", executionId, "tenant-a", 0, WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker-1", "claim-1", Now.AddMinutes(1)), 1, [], null, null, Now, Now, null, 0);

    private sealed class TestPayloadProtector : IWorkflowAlterationPayloadProtector
    {
        public ProtectedWorkflowAlterationPayload Protect(string planId, string tenantPartition, string canonicalRequestHash, string plaintext) =>
            new("test", "test", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext)));

        public string Unprotect(string planId, string tenantPartition, string canonicalRequestHash, ProtectedWorkflowAlterationPayload payload) =>
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload.Ciphertext));
    }

    private sealed class ThrowingUnprotectPayloadProtector : IWorkflowAlterationPayloadProtector
    {
        public ProtectedWorkflowAlterationPayload Protect(string planId, string tenantPartition, string canonicalRequestHash, string plaintext) =>
            throw new NotSupportedException();

        public string Unprotect(string planId, string tenantPartition, string canonicalRequestHash, ProtectedWorkflowAlterationPayload payload) =>
            throw new CryptographicException("The key is unavailable.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SealBeforeUnsealedCancellationStore(
        InMemoryWorkflowAlterationStore inner,
        DateTimeOffset sealedAt) : IWorkflowAlterationStore
    {
        private int _sealAttempted;

        public ValueTask<WorkflowAlterationPlanAdmissionResult> AdmitAsync(WorkflowAlterationPlanState plan, CancellationToken cancellationToken = default) =>
            inner.AdmitAsync(plan, cancellationToken);
        public ValueTask<WorkflowAlterationPlanState?> FindPlanAsync(string planId, CancellationToken cancellationToken = default) =>
            inner.FindPlanAsync(planId, cancellationToken);
        public ValueTask<WorkflowAlterationActivePlanPage> ListActivePlansAsync(int pageSize, string? cursor = null, CancellationToken cancellationToken = default) =>
            inner.ListActivePlansAsync(pageSize, cursor, cancellationToken);
        public ValueTask RescheduleActivePlanAsync(string planId, DateTimeOffset servicedAt, CancellationToken cancellationToken = default) =>
            inner.RescheduleActivePlanAsync(planId, servicedAt, cancellationToken);
        public ValueTask<WorkflowAlterationPlanState> CaptureAsync(string planId, long expectedRevision, IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets, string? nextCursor, CancellationToken cancellationToken = default) =>
            inner.CaptureAsync(planId, expectedRevision, targets, nextCursor, cancellationToken);
        public ValueTask<WorkflowAlterationPlanState> SealAsync(string planId, long expectedRevision, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            inner.SealAsync(planId, expectedRevision, at, cancellationToken);
        public async ValueTask<WorkflowAlterationPlanState> CancelUnsealedCaptureAsync(string planId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _sealAttempted, 1) == 0)
            {
                var plan = await inner.FindPlanAsync(planId, cancellationToken) ?? throw new KeyNotFoundException();
                await inner.SealAsync(planId, plan.Revision, sealedAt, cancellationToken);
            }
            return await inner.CancelUnsealedCaptureAsync(planId, cancelledAt, cancellationToken);
        }
        public ValueTask<WorkflowAlterationPlanState> FailUnsealedCaptureAsync(string planId, WorkflowAlterationSafeFailure safeFailure, DateTimeOffset failedAt, CancellationToken cancellationToken = default) =>
            inner.FailUnsealedCaptureAsync(planId, safeFailure, failedAt, cancellationToken);
        public ValueTask<WorkflowAlterationPlanState> RequestCancellationAsync(string planId, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) =>
            inner.RequestCancellationAsync(planId, requestedAt, cancellationToken);
        public ValueTask CancelPendingJobsAsync(string planId, IReadOnlyCollection<WorkflowAlterationOutcome> skippedOutcomes, DateTimeOffset completedAt, int maximumCount, CancellationToken cancellationToken = default) =>
            inner.CancelPendingJobsAsync(planId, skippedOutcomes, completedAt, maximumCount, cancellationToken);
        public ValueTask<WorkflowAlterationJobState?> FindJobAsync(string jobId, CancellationToken cancellationToken = default) =>
            inner.FindJobAsync(jobId, cancellationToken);
        public ValueTask<WorkflowAlterationJobCounts> GetJobCountsAsync(string planId, CancellationToken cancellationToken = default) =>
            inner.GetJobCountsAsync(planId, cancellationToken);
        public ValueTask<WorkflowAlterationJobState?> FindJobByCheckpointCommitIdAsync(string checkpointCommitId, CancellationToken cancellationToken = default) =>
            inner.FindJobByCheckpointCommitIdAsync(checkpointCommitId, cancellationToken);
        public ValueTask<WorkflowAlterationJobPage> PageJobsAsync(string planId, int pageSize, string? cursor = null, CancellationToken cancellationToken = default) =>
            inner.PageJobsAsync(planId, pageSize, cursor, cancellationToken);
        public ValueTask<WorkflowAlterationJobState?> ClaimNextAsync(string planId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default) =>
            inner.ClaimNextAsync(planId, ownerId, now, leaseDuration, cancellationToken);
        public ValueTask<WorkflowAlterationPlanState> ReconcileAsync(string planId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            inner.ReconcileAsync(planId, now, cancellationToken);
        public ValueTask ValidateTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default) =>
            inner.ValidateTerminalJobChangeAsync(change, cancellationToken);
        public ValueTask ApplyTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default) =>
            inner.ApplyTerminalJobChangeAsync(change, cancellationToken);
        public ValueTask CommitTerminalJobChangeAtomicallyAsync(
            WorkflowAlterationJobTerminalChange change,
            Func<CancellationToken, ValueTask> commitWorkflowCheckpointAsync,
            CancellationToken cancellationToken = default) =>
            inner.CommitTerminalJobChangeAtomicallyAsync(change, commitWorkflowCheckpointAsync, cancellationToken);
    }

    private sealed class RecordingActorProvider : IWorkflowExecutionActorProvider
    {
        public RecordingActor Actor { get; } = new();
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IWorkflowExecutionActor>(Actor);
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingActor : IWorkflowExecutionActor
    {
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public WorkflowExecutionActorDescriptor Descriptor { get; } = new("execution-1", "agent-1", "test", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.None, Now);
        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(envelope.EnvelopeId, envelope.WorkflowExecutionId, WorkflowExecutionCommandDispatchStatus.Accepted, Now));
        }
    }
}
