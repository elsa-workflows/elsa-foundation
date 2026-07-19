using Elsa.Persistence.Groundwork.Stores;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

// Behavioral assertions for the document-backed post-commit outbox bridge. The bridge reproduces the
// authoritative in-memory lifecycle (pending -> delivered / retryable / final) durably, so the same
// assertions run against both the real Groundwork SQLite provider and the in-memory document store.
public sealed class GroundworkRuntimePostCommitOutboxStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SavePending_Then_GetDeliverable_Returns_Item(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
        Assert.Equal(new[] { "item-1" }, deliverable.Select(x => x.OutboxItemId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SavePending_Is_Idempotent_For_Same_Intent(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));
        await store.SavePendingAsync(Pending("item-1", "wf-1")); // no throw

        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Nested_scheduler_identity_is_compacted_within_the_portable_projection_limit(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var sourceWorkItemId = string.Join(':', Enumerable.Repeat("schedule:node-sequence:start:activity:invoke", 6));
        var targetWorkItemId = $"{sourceWorkItemId}:schedule-child:node-sync-endpoint:activity-scheduled";
        var commitId = $"commit:wf-1:{sourceWorkItemId}:activity-started";
        var intent = new RuntimePostCommitIntent(
            intentId: $"{sourceWorkItemId}:post-commit:{targetWorkItemId}",
            workflowExecutionId: "wf-1",
            kind: "scheduler",
            recordedAt: Now,
            activityExecutionId: "activity-1",
            idempotencyKey: null,
            payload: null);
        var logicalOutboxItemId = RuntimePostCommitOutboxItems.OutboxItemId(commitId, intent);
        Assert.True(logicalOutboxItemId.Length <= RuntimePostCommitOutboxIdentity.MaximumLength);
        Assert.Equal(logicalOutboxItemId, RuntimePostCommitOutboxItems.OutboxItemId(commitId, intent));
        var pending = new RuntimePostCommitOutboxItem(
            logicalOutboxItemId,
            intent,
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now);

        await store.SavePendingAsync(pending);
        await store.SavePendingAsync(pending);

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
        Assert.Equal(logicalOutboxItemId, Assert.Single(deliverable).OutboxItemId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Exact_lookup_uses_the_scoped_physical_identity_mapping(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var outboxItemId = new string('x', 451);
        await store.SavePendingAsync(Pending(outboxItemId, "wf-lookup"));

        var found = await ((IPostCommitOutboxLookupStore)store).FindAsync(outboxItemId);
        var envelope = await fixture.DocumentStore.LoadAsync(
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind,
            GroundworkPhysicalDocumentIdTestData.PhysicalAliasFor(outboxItemId));
        using var content = JsonDocument.Parse(envelope!.ContentJson);

        Assert.NotNull(found);
        Assert.Equal(outboxItemId, found.OutboxItemId);
        Assert.Equal("wf-lookup", found.Intent.WorkflowExecutionId);
        Assert.Equal(
            outboxItemId,
            content.RootElement.GetProperty("logicalOutboxItemId").GetString());
        Assert.True(
            content.RootElement.GetProperty("item").GetProperty("outboxItemId").GetString()!.Length <=
            RuntimePostCommitOutboxIdentity.MaximumLength);
        Assert.Null(await ((IPostCommitOutboxLookupStore)store).FindAsync("missing"));
    }

    [Theory]
    [InlineData(450)]
    [InlineData(451)]
    public async Task Portable_identity_boundary_round_trips(int identityLength)
    {
        await using var fixture = CreateStore("sqlite");
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var outboxItemId = new string('x', identityLength);

        await store.SavePendingAsync(Pending(outboxItemId, "wf-1"));

        Assert.Equal(outboxItemId, Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10))).OutboxItemId);
    }

    [Fact]
    public async Task Physical_alias_collision_fails_closed()
    {
        await using var fixture = CreateStore("sqlite");
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var longLogicalId = new string('x', 451);
        var collidingShortLogicalId = GroundworkPhysicalDocumentIdTestData.PhysicalAliasFor(longLogicalId);
        await store.SavePendingAsync(Pending(longLogicalId, "wf-long"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.SavePendingAsync(Pending(collidingShortLogicalId, "wf-short")));

        Assert.Contains("physical document identity collision", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SavePending_With_Conflicting_Intent_Throws(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1", kind: "a"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.SavePendingAsync(Pending("item-1", "wf-1", kind: "b")));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RecordDelivered_Makes_Item_Terminal_And_Undeliverable(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now));

        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
        // A second result on a terminal item is rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RetryableFailure_Becomes_Deliverable_After_Delay_Then_Final(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        var retry = new RuntimePostCommitRetryPolicy(maxAttempts: 2, delay: TimeSpan.FromMinutes(5));
        await store.SavePendingAsync(Pending("item-1", "wf-1", retryPolicy: retry));

        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.FailedRetryable, Now, "boom"));

        // Not yet available (within the retry delay).
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
        // Available again after the delay window.
        var afterDelay = Now.AddMinutes(6);
        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(afterDelay, 10)));

        // Exhausting the retry budget promotes the failure to final and removes it from the deliverable set.
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.FailedRetryable, afterDelay, "boom again"));
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(afterDelay.AddMinutes(10), 10)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task GetDeliverable_Filters_By_Workflow_Execution(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));
        await store.SavePendingAsync(Pending("item-2", "wf-2"));

        var forWf1 = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10, workflowExecutionId: "wf-1"));
        Assert.Equal(new[] { "item-1" }, forWf1.Select(x => x.OutboxItemId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task GetDeliverable_Filters_By_Intent_Kind(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1", kind: "scheduler"));
        await store.SavePendingAsync(Pending("item-2", "wf-1", kind: "signal"));

        var schedulerItems = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10, intentKind: "scheduler"));
        Assert.Equal(new[] { "item-1" }, schedulerItems.Select(x => x.OutboxItemId));
    }

    [Fact]
    public async Task GetDeliverable_SelectsEarliestCandidateAcrossBoundedProviderPages()
    {
        await using var fixture = CreateStore("memory");
        var seedStore = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);
        for (var index = 0; index < 300; index++)
            await seedStore.SavePendingAsync(PendingAt($"a-{index:D3}", Now.AddHours(1)));
        await seedStore.SavePendingAsync(PendingAt("z-earliest", Now));
        var recording = new RecordingBoundedDocumentStore(fixture.BoundedDocumentStore);
        var store = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            recording);

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddHours(2), 1));

        Assert.Equal("z-earliest", Assert.Single(deliverable).OutboxItemId);
        Assert.Equal(4, recording.Queries.Count);
        Assert.All(recording.Queries, query => Assert.Equal(1, query.Take));
        Assert.InRange(recording.ReturnedDocumentCounts.Sum(), 1, 4);
    }

    [Fact]
    public async Task Claim_limit_bounds_total_provider_results_independent_of_history()
    {
        await using var fixture = CreateStore("memory");
        var seedStore = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);
        for (var index = 0; index < 300; index++)
            await seedStore.SavePendingAsync(PendingAt($"future-{index:D3}", Now.AddHours(1)));
        await seedStore.SavePendingAsync(PendingAt("due-now", Now));
        var recording = new RecordingBoundedDocumentStore(fixture.BoundedDocumentStore);
        var store = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            recording);

        var claim = Assert.Single(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest(
                "bounded-owner",
                Now,
                TimeSpan.FromMinutes(1),
                1)));

        Assert.Equal("due-now", claim.OutboxItemId);
        Assert.Equal(5, recording.Queries.Count);
        Assert.All(recording.Queries, query => Assert.Equal(1, query.Take));
        Assert.InRange(recording.ReturnedDocumentCounts.Sum(), 1, 5);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Null_available_at_remains_immediately_deliverable_and_claimable(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);
        for (var index = 0; index < 25; index++)
            await store.SavePendingAsync(PendingAt($"future-{index:D2}", Now, Now.AddHours(1)));
        await store.SavePendingAsync(PendingWithoutAvailability("immediate-null"));

        var deliverable = Assert.Single(await store.GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(Now, 1)));
        var claim = Assert.Single(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest(
                "null-owner",
                Now,
                TimeSpan.FromMinutes(1),
                1)));

        Assert.Equal("immediate-null", deliverable.OutboxItemId);
        Assert.Equal("immediate-null", claim.OutboxItemId);
        Assert.Null(deliverable.AvailableAt);
        Assert.Null(claim.Item.AvailableAt);
    }

    [Fact]
    public async Task Maximum_limit_does_not_overflow_candidate_collection_capacity()
    {
        await using var fixture = CreateStore("memory");
        var recording = new EmptyRecordingBoundedDocumentStore();
        var store = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            recording);

        Assert.Empty(await store.GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(Now, int.MaxValue)));
        Assert.Equal(4, recording.Queries.Count);
        Assert.All(recording.Queries, query => Assert.Equal(int.MaxValue, query.Take));
        Assert.All(
            recording.Queries.Where(query =>
                query.QueryIdentity.StartsWith("list-immediate", StringComparison.Ordinal)),
            query =>
            {
                var availability = query.Clauses
                    .SelectMany(clause => clause.Comparisons)
                    .Single(comparison =>
                        comparison.Path == ElsaRuntimeStorageManifest.PostCommitOutboxAvailableAtField);
                Assert.Equal(QueryComparisonOperator.Equal, availability.Operator);
                Assert.Null(Assert.Single(availability.Values));
            });

        recording.Queries.Clear();
        Assert.Empty(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest(
                "maximum-owner",
                Now,
                TimeSpan.FromMinutes(1),
                int.MaxValue)));
        Assert.Equal(5, recording.Queries.Count);
        Assert.All(recording.Queries, query => Assert.Equal(int.MaxValue, query.Take));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Claim_orders_fresh_and_expired_work_by_their_next_claimable_time(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);
        await store.SavePendingAsync(PendingAt(
            "expired-claim",
            Now.AddHours(-10),
            Now.AddHours(-10)));
        await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "original-owner",
            Now.AddMinutes(-2),
            TimeSpan.FromMinutes(1),
            1));
        await store.SavePendingAsync(PendingAt(
            "fresh-pending",
            Now.AddHours(-3),
            Now.AddMinutes(-2)));

        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "next-owner",
            Now,
            TimeSpan.FromMinutes(1),
            1)));

        Assert.Equal("fresh-pending", claim.OutboxItemId);
    }

    [Fact]
    public async Task Pending_Item_Survives_Restart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-outbox-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                await store.SavePendingAsync(Pending("item-1", "wf-1"));
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
                Assert.Equal(new[] { "item-1" }, deliverable.Select(x => x.OutboxItemId));
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Expired_claim_is_reclaimed_with_a_higher_fencing_token_and_stale_owner_is_rejected(string provider)
    {
        await using var fixture = CreateStore(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore);
        await store.SavePendingAsync(Pending("item-1", "wf-1"));

        var first = Assert.Single(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var second = Assert.Single(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-2", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));

        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, second.Item.Status);
        Assert.Equal(first.FencingToken + 1, second.FencingToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.RecordDeliveryResultAsync(
                first,
                new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2))));

        await store.RecordDeliveryResultAsync(
            second,
            new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2)));
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddHours(1), 10)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Final_child_start_failure_atomically_completes_claim_and_projects_dispatch_failed(string provider)
    {
        await using var fixture = CreateStore(provider);
        var access = GroundworkTestAccess.DefaultAccessContextAccessor;
        var outbox = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore,
            access);
        var dispatches = new GroundworkWorkflowDispatchStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            access,
            fixture.BoundedDocumentStore);
        var pendingDispatch = GroundworkWorkflowDispatchStoreTests.Pending("parent-1", "activity-1");
        await dispatches.SaveAsync(pendingDispatch);
        await outbox.SavePendingAsync(PendingDispatch("item-1", pendingDispatch));
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            "item-1",
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "classified-child-start-failure");

        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            failure,
            pendingDispatch.TransitionToDispatchFailed(Now.AddSeconds(1))));

        Assert.Equal(
            WorkflowDispatchStatus.DispatchFailed,
            (await dispatches.FindAsync(pendingDispatch.DispatchId))!.Status);
        Assert.Empty(await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddHours(1), 10)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Claimed_child_start_failure_cannot_override_a_visible_matching_child(string provider)
    {
        await using var fixture = CreateStore(provider);
        var access = GroundworkTestAccess.DefaultAccessContextAccessor;
        var outbox = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore,
            access);
        var dispatches = new GroundworkWorkflowDispatchStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            access,
            fixture.BoundedDocumentStore);
        var executions = new GroundworkWorkflowExecutionStateStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            access);
        var pendingDispatch = GroundworkWorkflowDispatchStoreTests.Pending(
            "parent-visible",
            "activity-visible",
            mode: WorkflowDispatchMode.WaitForCompletion);
        var pendingStart = PendingDispatch("item-visible", pendingDispatch);
        await dispatches.SaveAsync(pendingDispatch);
        await outbox.SavePendingAsync(pendingStart);
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            pendingStart.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "secret stale failure");
        var projection = Assert.IsType<PostCommitFailureProjection>(
            await new WorkflowDispatchDeliveryFailureProjector(dispatches).ProjectAsync(claim.Item, failure));
        await executions.SaveAsync(ChildState(pendingDispatch));

        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            failure,
            projection.WorkflowDispatch,
            projection.FollowUpOutboxItem));

        var completed = Assert.IsType<RuntimePostCommitOutboxItem>(await outbox.FindAsync(pendingStart.OutboxItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, completed.Status);
        Assert.Null(completed.LastFailureMessage);
        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatches.FindAsync(pendingDispatch.DispatchId))!.Status);
        var followUpId = new WorkflowDispatchIdentity("parent-visible", "activity-visible")
            .WaitFailureResumeOutboxItemId(0);
        Assert.Null(await outbox.FindAsync(followUpId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Invalid_dispatch_projection_rolls_back_outbox_completion(string provider)
    {
        await using var fixture = CreateStore(provider);
        var access = GroundworkTestAccess.DefaultAccessContextAccessor;
        var outbox = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore,
            access);
        var dispatches = new GroundworkWorkflowDispatchStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            access,
            fixture.BoundedDocumentStore);
        var pendingDispatch = GroundworkWorkflowDispatchStoreTests.Pending("parent-1", "activity-1");
        await dispatches.SaveAsync(pendingDispatch);
        await outbox.SavePendingAsync(PendingDispatch("item-1", pendingDispatch));
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var safeFailure = pendingDispatch.TransitionToDispatchFailed(Now.AddSeconds(1));
        var invalidProjection = new WorkflowDispatchRecord(
            pendingDispatch.DispatchId,
            pendingDispatch.ParentWorkflowExecutionId,
            pendingDispatch.ParentActivityExecutionId,
            pendingDispatch.ChildWorkflowExecutionId,
            pendingDispatch.ChildExecutable,
            pendingDispatch.ChildSource,
            pendingDispatch.Mode,
            WorkflowDispatchStatus.DispatchFailed,
            "conflicting-correlation",
            pendingDispatch.TenantId,
            pendingDispatch.Partition,
            pendingDispatch.RunKind,
            pendingDispatch.Authority,
            pendingDispatch.InputDescriptors,
            pendingDispatch.CreatedAt,
            Now.AddSeconds(1),
            safeFailure.Metadata);
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            "item-1",
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "classified-child-start-failure");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                claim,
                failure,
                invalidProjection)));

        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatches.FindAsync(pendingDispatch.DispatchId))!.Status);
        await outbox.RecordDeliveryResultAsync(
            claim,
            new RuntimePostCommitOutboxDeliveryResult("item-1", RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(2)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Wait_exhaustion_atomically_persists_dead_letter_dispatch_and_resume_across_recreation(string provider)
    {
        await using var fixture = CreateStore(provider);
        var access = GroundworkTestAccess.DefaultAccessContextAccessor;
        var outbox = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore,
            access);
        var dispatches = new GroundworkWorkflowDispatchStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            access,
            fixture.BoundedDocumentStore);
        var pendingDispatch = GroundworkWorkflowDispatchStoreTests.Pending(
            "parent-wait",
            "activity-wait",
            mode: WorkflowDispatchMode.WaitForCompletion);
        var pendingStart = PendingDispatch("item-wait", pendingDispatch);
        await dispatches.SaveAsync(pendingDispatch);
        await outbox.SavePendingAsync(pendingStart);
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            pendingStart.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "secret provider failure");
        var projection = Assert.IsType<PostCommitFailureProjection>(
            await new WorkflowDispatchDeliveryFailureProjector(dispatches).ProjectAsync(claim.Item, failure));

        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            failure,
            projection.WorkflowDispatch,
            projection.FollowUpOutboxItem));

        var recoveredOutbox = new GroundworkRuntimePostCommitOutboxStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            fixture.BoundedDocumentStore,
            access);
        var recoveredDispatches = new GroundworkWorkflowDispatchStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            access,
            fixture.BoundedDocumentStore);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await recoveredOutbox.FindAsync(pendingStart.OutboxItemId))!.Status);
        var failed = Assert.IsType<WorkflowDispatchRecord>(await recoveredDispatches.FindAsync(pendingDispatch.DispatchId));
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, failed.Status);
        Assert.DoesNotContain(failed.Metadata.Values, value => value.Contains("secret", StringComparison.OrdinalIgnoreCase));
        var followUpId = new WorkflowDispatchIdentity("parent-wait", "activity-wait").WaitFailureResumeOutboxItemId(0);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await recoveredOutbox.FindAsync(followUpId))!.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recoveredOutbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                claim,
                failure,
                projection.WorkflowDispatch,
                projection.FollowUpOutboxItem)).AsTask());
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await recoveredOutbox.FindAsync(pendingStart.OutboxItemId))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await recoveredOutbox.FindAsync(followUpId))!.Status);
    }

    private static RuntimePostCommitOutboxItem Pending(
        string outboxItemId,
        string workflowExecutionId,
        string kind = "publish",
        RuntimePostCommitRetryPolicy? retryPolicy = null) => new(
        outboxItemId: outboxItemId,
        intent: new RuntimePostCommitIntent(
            intentId: $"intent-{outboxItemId}",
            workflowExecutionId: workflowExecutionId,
            kind: kind,
            recordedAt: Now,
            activityExecutionId: null,
            idempotencyKey: null,
            payload: null),
        status: RuntimePostCommitOutboxStatus.Pending,
        recordedAt: Now,
        availableAt: Now,
        retryPolicy: retryPolicy);

    private static WorkflowExecutionState ChildState(WorkflowDispatchRecord dispatch) => new(
        dispatch.ChildWorkflowExecutionId,
        dispatch.ChildExecutable,
        WorkflowExecutionStatus.Running,
        null,
        Now,
        Now,
        Now,
        null,
        dispatch.CorrelationId,
        dispatch.ParentWorkflowExecutionId,
        dispatch.TenantId,
        new Dictionary<string, string>())
    {
        RunKind = dispatch.RunKind,
        PinnedSource = dispatch.ChildSource,
        Partition = dispatch.Partition,
        Authority = dispatch.Authority
    };

    private static RuntimePostCommitOutboxItem PendingAt(
        string outboxItemId,
        DateTimeOffset recordedAt,
        DateTimeOffset? availableAt = null) => new(
        outboxItemId,
        new RuntimePostCommitIntent(
            $"intent-{outboxItemId}",
            "wf-paged",
            "publish",
            recordedAt,
            activityExecutionId: null,
            idempotencyKey: null,
            payload: null),
        RuntimePostCommitOutboxStatus.Pending,
        recordedAt,
        availableAt ?? Now);

    private static RuntimePostCommitOutboxItem PendingWithoutAvailability(string outboxItemId) => new(
        outboxItemId,
        new RuntimePostCommitIntent(
            $"intent-{outboxItemId}",
            "wf-paged",
            "publish",
            Now,
            activityExecutionId: null,
            idempotencyKey: null,
            payload: null),
        RuntimePostCommitOutboxStatus.Pending,
        Now,
        availableAt: null);

    private static RuntimePostCommitOutboxItem PendingDispatch(
        string outboxItemId,
        WorkflowDispatchRecord dispatch) => new(
        outboxItemId,
        new RuntimePostCommitIntent(
            new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).StartIntentId,
            dispatch.ParentWorkflowExecutionId,
            DispatchWorkflowConstants.StartChildIntentKind,
            Now,
            dispatch.ParentActivityExecutionId,
            new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).StartIdempotencyKey,
            payload: null,
            metadata: new Dictionary<string, string> { ["runtime.dispatchId"] = dispatch.DispatchId }),
        RuntimePostCommitOutboxStatus.Pending,
        Now,
        Now,
        RuntimePostCommitRetryPolicy.None);

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);
}
