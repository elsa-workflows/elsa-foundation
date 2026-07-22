using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class DispatchWorkflowDeliveryRecoveryCrashTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Wait_exhaustion_and_uncertain_resume_ack_converge_after_recreation()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("sqlite");
        var seed = await SeedExhaustedAsync(fixture, WorkflowDispatchMode.WaitForCompletion);
        var recovered = Stores(fixture);
        var followUpId = seed.Identity.WaitFailureResumeOutboxItemId(0);
        var followUp = Assert.IsType<RuntimePostCommitOutboxItem>(await recovered.Outbox.FindAsync(followUpId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, followUp.Status);
        var firstClaim = Assert.Single(await recovered.Outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "resume-owner-before-crash",
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(1),
            1,
            intentKind: DispatchWorkflowConstants.ResumeParentIntentKind)));

        var afterCrash = Stores(fixture);
        var recoveredClaim = Assert.Single(await afterCrash.Outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "resume-owner-after-crash",
            Now.AddMinutes(3),
            TimeSpan.FromMinutes(1),
            1,
            intentKind: DispatchWorkflowConstants.ResumeParentIntentKind)));
        Assert.True(recoveredClaim.FencingToken > firstClaim.FencingToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => afterCrash.Outbox.RecordDeliveryResultAsync(
            firstClaim,
            new RuntimePostCommitOutboxDeliveryResult(followUpId, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(3))).AsTask());

        var consumingDispatcher = new ConsumingBookmarkResumeDispatcher(
            afterCrash.Bookmarks,
            afterCrash.Activities,
            seed.Identity,
            Now.AddMinutes(3));
        await new ParentResumeExecutor(
            consumingDispatcher,
            afterCrash.Workflows,
            afterCrash.Activities,
            afterCrash.Bookmarks).HandleAsync(recoveredClaim.Item.Intent);
        Assert.Null(await afterCrash.Bookmarks.FindAsync(seed.Dispatch.ParentWorkflowExecutionId, seed.Identity.WaitBookmarkId));
        Assert.Equal(
            ActivityExecutionStatus.Completed,
            (await afterCrash.Activities.FindAsync(seed.Dispatch.ParentWorkflowExecutionId, seed.Dispatch.ParentActivityExecutionId))!.Status);

        // Simulate response/ack loss after the durable bookmark consumption. A new claimant replays the
        // real executor idempotently and is the only owner allowed to acknowledge the outbox item.
        var afterConsumptionCrash = Stores(fixture);
        var postConsumptionClaim = Assert.Single(await afterConsumptionCrash.Outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "resume-owner-after-consumption-crash",
            Now.AddMinutes(5),
            TimeSpan.FromMinutes(1),
            1,
            intentKind: DispatchWorkflowConstants.ResumeParentIntentKind)));
        await new ParentResumeExecutor(
            new ConsumingBookmarkResumeDispatcher(
                afterConsumptionCrash.Bookmarks,
                afterConsumptionCrash.Activities,
                seed.Identity,
                Now.AddMinutes(5)),
            afterConsumptionCrash.Workflows,
            afterConsumptionCrash.Activities,
            afterConsumptionCrash.Bookmarks).HandleAsync(postConsumptionClaim.Item.Intent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => afterConsumptionCrash.Outbox.RecordDeliveryResultAsync(
            recoveredClaim,
            new RuntimePostCommitOutboxDeliveryResult(followUpId, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(5))).AsTask());
        await afterConsumptionCrash.Outbox.RecordDeliveryResultAsync(
            postConsumptionClaim,
            new RuntimePostCommitOutboxDeliveryResult(followUpId, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(5)));
        var final = Stores(fixture);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await final.Outbox.FindAsync(seed.Start.OutboxItemId))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await final.Outbox.FindAsync(followUpId))!.Status);
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await final.Dispatches.FindAsync(seed.Dispatch.DispatchId));
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, dispatch.Status);
        Assert.False(WorkflowDispatchLifecycle.IsRedriveEligible(dispatch));
    }

    [Fact]
    public async Task Redrive_response_loss_expired_claim_admission_and_ack_converge_after_recreation()
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create("sqlite");
        var seed = await SeedExhaustedAsync(fixture, WorkflowDispatchMode.FireAndForget);
        var request = new WorkflowDispatchRedriveRequest(seed.Dispatch.DispatchId, "crash-redrive-1", Now.AddMinutes(1));

        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, (await seed.Outbox.RedriveAsync(request)).Disposition);
        var afterResponseLoss = Stores(fixture);
        Assert.Equal(WorkflowDispatchRedriveDisposition.AlreadyApplied, (await afterResponseLoss.Outbox.RedriveAsync(request)).Disposition);
        var firstClaim = Assert.Single(await afterResponseLoss.Outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "start-owner-before-crash",
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(1),
            1,
            intentKind: DispatchWorkflowConstants.StartChildIntentKind)));

        var afterClaimCrash = Stores(fixture);
        var recoveredClaim = Assert.Single(await afterClaimCrash.Outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "start-owner-after-crash",
            Now.AddMinutes(3),
            TimeSpan.FromMinutes(1),
            1,
            intentKind: DispatchWorkflowConstants.StartChildIntentKind)));
        Assert.True(recoveredClaim.FencingToken > firstClaim.FencingToken);
        var admission = await afterClaimCrash.Dispatches.TryAdmitAsync(seed.Dispatch.DispatchId, Now.AddMinutes(3));
        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admission.Disposition);
        Assert.Equal(seed.Identity.ChildWorkflowExecutionId, admission.Record.ChildWorkflowExecutionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => afterClaimCrash.Outbox.RecordDeliveryResultAsync(
            firstClaim,
            new RuntimePostCommitOutboxDeliveryResult(seed.Start.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(3))).AsTask());
        await afterClaimCrash.Outbox.RecordDeliveryResultAsync(
            recoveredClaim,
            new RuntimePostCommitOutboxDeliveryResult(seed.Start.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(3)));

        var final = Stores(fixture);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await final.Outbox.FindAsync(seed.Start.OutboxItemId))!.Status);
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await final.Dispatches.FindAsync(seed.Dispatch.DispatchId));
        Assert.Equal(WorkflowDispatchStatus.Started, dispatch.Status);
        Assert.Equal(1, WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch));
        Assert.Equal(seed.Identity.ChildWorkflowExecutionId, dispatch.ChildWorkflowExecutionId);
    }

    private static async Task<Seed> SeedExhaustedAsync(
        GroundworkDocumentStoreFixture fixture,
        WorkflowDispatchMode mode)
    {
        var stores = Stores(fixture);
        var dispatch = NewDispatch(mode);
        var identity = new WorkflowDispatchIdentity(
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId);
        var start = new RuntimePostCommitOutboxItem(
            $"outbox:{dispatch.ParentActivityExecutionId}",
            new RuntimePostCommitIntent(
                identity.StartIntentId,
                dispatch.ParentWorkflowExecutionId,
                DispatchWorkflowConstants.StartChildIntentKind,
                Now,
                dispatch.ParentActivityExecutionId,
                identity.StartIdempotencyKey,
                System.Text.Json.JsonSerializer.SerializeToElement(new { input = "safe" }),
                new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                    [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
                }),
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now,
            new RuntimePostCommitRetryPolicy(2, TimeSpan.FromSeconds(1)));
        await stores.Dispatches.SaveAsync(dispatch);
        await stores.Outbox.SavePendingAsync(start);
        if (mode == WorkflowDispatchMode.WaitForCompletion)
            await SeedWaitParentAsync(stores, dispatch, identity);
        var firstProcessor = Processor(stores, Now);
        var firstAttempt = await firstProcessor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1));
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, Assert.Single(firstAttempt.Items).RequestedDeliveryResultStatus);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, (await stores.Outbox.FindAsync(start.OutboxItemId))!.Status);

        // Recreate every adapter after the durable retry decision, then exhaust through the real processor.
        var afterRetryCrash = Stores(fixture);
        var secondProcessor = Processor(afterRetryCrash, Now.AddSeconds(2));
        var secondAttempt = await secondProcessor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(1));
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, Assert.Single(secondAttempt.Items).RequestedDeliveryResultStatus);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await afterRetryCrash.Outbox.FindAsync(start.OutboxItemId))!.Status);
        return new Seed(afterRetryCrash.Outbox, dispatch, start, identity);
    }

    private static RuntimePostCommitOutboxProcessor Processor(StorePair stores, DateTimeOffset now) =>
        new(
            stores.Outbox,
            new AlwaysFailingIntentDispatcher(),
            new FixedTimeProvider(now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            stores.Dispatches,
            [new WorkflowDispatchDeliveryFailureProjector(stores.Dispatches)],
            logger: null);

    private static StorePair Stores(GroundworkDocumentStoreFixture fixture)
    {
        var access = GroundworkTestAccess.DefaultAccessContextAccessor;
        return new StorePair(
            new GroundworkRuntimePostCommitOutboxStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                fixture.BoundedDocumentStore,
                access),
            new GroundworkWorkflowDispatchStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                access,
                fixture.BoundedDocumentStore),
            new GroundworkWorkflowExecutionStateStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                access,
                boundedStore: fixture.BoundedDocumentStore),
            new GroundworkActivityExecutionStateStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                fixture.BoundedDocumentStore),
            new GroundworkBookmarkStateStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                fixture.BoundedDocumentStore));
    }

    private static async Task SeedWaitParentAsync(
        StorePair stores,
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchIdentity identity)
    {
        await stores.Workflows.SaveAsync(new WorkflowExecutionState(
            dispatch.ParentWorkflowExecutionId,
            new WorkflowExecutableIdentity("parent-artifact", "parent-definition", "parent-version", "1", "parent-hash"),
            WorkflowExecutionStatus.Running,
            null,
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            Now,
            null,
            null,
            null,
            dispatch.TenantId,
            new Dictionary<string, string>()));
        var execution = new ActivityExecution(
            dispatch.ParentActivityExecutionId,
            dispatch.ParentWorkflowExecutionId,
            "dispatch-node",
            "dispatch",
            DispatchWorkflowConstants.ActivityType,
            "1.0.0");
        await stores.Activities.SaveAsync(new ActivityExecutionState(
            execution,
            ActivityExecutionStatus.Suspended,
            null,
            1,
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            null,
            null,
            null,
            null,
            null,
            ActivitySchedulingProvenance.From(
                dispatch.ParentWorkflowExecutionId,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            0,
            [identity.WaitBookmarkId],
            [],
            0,
            0,
            new Dictionary<string, string>()));
        await stores.Bookmarks.SaveAsync(new BookmarkState(
            identity.WaitBookmarkId,
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId,
            "dispatch-node",
            WorkflowExecutableResumeTarget.ComposeScopedId("dispatch-node", DispatchWorkflowConstants.CompletionResumeTargetId),
            DispatchWorkflowConstants.WaitStimulusType,
            identity.WaitStimulusHash,
            null,
            new Dictionary<string, string>(),
            Now,
            null));
    }

    private static WorkflowDispatchRecord NewDispatch(WorkflowDispatchMode mode)
    {
        var parentId = $"parent-{Guid.NewGuid():N}";
        var activityId = $"activity-{Guid.NewGuid():N}";
        var identity = new WorkflowDispatchIdentity(parentId, activityId);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentId,
            activityId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1", "hash-1"),
            new WorkflowExecutableSourceProvenance("source-1", "WorkflowDefinitionVersion", "version-1", "1", "definition-1", "version-1", "1", "publication-1", "slot-1"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            null,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parentId, "initiator-1"),
            [],
            Now,
            Now);
    }

    private sealed record StorePair(
        GroundworkRuntimePostCommitOutboxStore Outbox,
        GroundworkWorkflowDispatchStore Dispatches,
        GroundworkWorkflowExecutionStateStore Workflows,
        GroundworkActivityExecutionStateStore Activities,
        GroundworkBookmarkStateStore Bookmarks);

    private sealed class ConsumingBookmarkResumeDispatcher(
        IBookmarkStateStore bookmarkStore,
        IActivityExecutionStateStore activityStore,
        WorkflowDispatchIdentity identity,
        DateTimeOffset completedAt) : IBookmarkResumeDispatcher
    {
        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            var bookmark = await bookmarkStore.FindAsync(request.WorkflowExecutionId, identity.WaitBookmarkId, cancellationToken);
            if (bookmark is not null)
            {
                await bookmarkStore.DeleteAsync(request.WorkflowExecutionId, identity.WaitBookmarkId, cancellationToken);
                var activity = await activityStore.FindAsync(request.WorkflowExecutionId, bookmark.ActivityExecutionId, cancellationToken)
                    ?? throw new InvalidOperationException("The waited dispatch activity was not found.");
                await activityStore.SaveAsync(activity with
                {
                    Status = ActivityExecutionStatus.Completed,
                    CompletedAt = completedAt,
                    BookmarkIds = []
                }, cancellationToken);
            }

            return new BookmarkResumeDispatchResult(
                BookmarkResumeDispatchStatus.Duplicate,
                request.WorkflowExecutionId,
                reason: "already-consumed");
        }
    }

    private sealed class AlwaysFailingIntentDispatcher : IRuntimePostCommitIntentDispatcher
    {
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider details must not affect durable delivery diagnostics");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Seed(
        GroundworkRuntimePostCommitOutboxStore Outbox,
        WorkflowDispatchRecord Dispatch,
        RuntimePostCommitOutboxItem Start,
        WorkflowDispatchIdentity Identity);
}
