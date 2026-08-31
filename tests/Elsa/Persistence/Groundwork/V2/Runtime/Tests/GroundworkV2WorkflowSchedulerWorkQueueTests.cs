using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2WorkflowSchedulerWorkQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_preserves_idempotent_fifo_restart_and_distinct_pending_ids()
    {
        await using var runtime = NativeProviderRuntime.Create();
        var accessor = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        using (var connection = runtime.OpenConnection())
        {
            var source = new DirectSessionSource(connection);
            connection.Schema.Apply(unit);
            IWorkflowSchedulerWorkQueue queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, accessor);

            await queue.EnqueueAsync(Item("wf-b", "work-2", 2, recordedAt: Now));
            await queue.EnqueueAsync(Item("wf-b", "work-1", 1, recordedAt: Now));
            await queue.EnqueueAsync(Item("wf-a", "work-3", 3));
            await queue.EnqueueAsync(Item("a:b", "c", 4, recordedAt: Now));
            await queue.EnqueueAsync(Item("a", "b:c", 5, recordedAt: Now));
            var duplicate = await queue.EnqueueAsync(Item("wf-b", "work-1", 99, commandId: "different-command"));

            Assert.Equal("command-1", duplicate.CommandId);
            var first = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf-b", 1));
            var second = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf-b", 1, first.NextContinuationToken));
            Assert.Equal(["work-1"], first.Items.Select(item => item.WorkItemId));
            Assert.Equal(["work-2"], second.Items.Select(item => item.WorkItemId));
            Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("a:b"))).Items);
            Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("a"))).Items);

            var tenantB = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
            IWorkflowSchedulerWorkQueue queueB = new GroundworkV2WorkflowSchedulerWorkQueue(source, tenantB);
            var sameLogicalId = await queueB.EnqueueAsync(Item("wf-b", "work-1", 10, commandId: "tenant-b-command"));
            Assert.Equal("tenant-b-command", sameLogicalId.CommandId);
            Assert.Equal("tenant-b-command", (await queueB.ListAsync(new RuntimeSchedulerWorkQuery("wf-b"))).Items[0].CommandId);
            Assert.Equal("command-1", (await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf-b"))).Items[0].CommandId);

            Assert.Equal(["a", "a:b", "wf-a", "wf-b"], await queue.ListPendingWorkflowExecutionIdsAsync(10));
            Assert.Equal(["a", "a:b"], await queue.ListPendingWorkflowExecutionIdsAsync(2));
        }

        // A fresh provider connection proves the durable rows, rather than an in-process session cache, survive restart.
        using (var reopenedConnection = runtime.OpenConnection())
        {
            var reopenedSource = new DirectSessionSource(reopenedConnection);
            IWorkflowSchedulerWorkQueue restarted = new GroundworkV2WorkflowSchedulerWorkQueue(reopenedSource, accessor);
            Assert.Equal("work-1", (await restarted.DequeueAsync("wf-b"))!.WorkItemId);
            Assert.Equal("work-2", (await restarted.DequeueAsync("wf-b"))!.WorkItemId);
            Assert.Null(await restarted.DequeueAsync("wf-b"));
        }
    }

    [Fact]
    public async Task Sqlite_long_identity_uses_hashed_alias_and_collision_fails_closed()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        connection.Schema.Apply(unit);
        var accessor = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, accessor);
        var workItemId = new string('x', ElsaRuntimeV2StorageManifest.IdMaximumLength);
        var item = Item("wf-long", workItemId, 1);

        await queue.EnqueueAsync(item);
        var rowValues = source.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Query(new QueryRequest(
                new TableId(unit.Name),
                Predicate.AlwaysTrue.Instance,
                [],
                Projection.All,
                Paging.Keyset(10)))
            .Rows.Single();
        var physicalId = (string)rowValues[ElsaRuntimeV2StorageManifest.IdField]!;
        Assert.True(physicalId.Length <= ElsaRuntimeV2StorageManifest.IdMaximumLength);
        Assert.NotEqual(item.WorkItemId, physicalId);
        Assert.Equal(item.WorkItemId, Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf-long"))).Items).WorkItemId);

        var row = source.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Read(GroundworkRuntimeRowStore.Key(physicalId));
        Assert.NotNull(row);
        var collision = Item("wf-other", "work-other", 2);
        var foreignEnvelope = GroundworkV2SchedulerWorkStorageConventions.NewEnvelope(collision);
        var foreignValues = GroundworkV2SchedulerWorkStorageConventions.Values(foreignEnvelope).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreignValues[ElsaRuntimeV2StorageManifest.IdField] = physicalId;
        var wrong = new StorageValues(foreignValues);
        var session = source.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope("tenant-a")));
        Assert.Equal(WriteOutcomeStatus.Upserted, session.Upsert(wrong, WriteOptions.Unconditional).Status);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => queue.DeleteAsync(item.WorkflowExecutionId, item.WorkItemId).AsTask());
        Assert.Contains("physical identity collision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_claims_are_fifo_fenced_and_consumption_survives_renewal()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var accessor = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IWorkflowSchedulerWorkQueue queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, accessor);
        await queue.EnqueueAsync(Item("wf", "one", 1));
        await queue.EnqueueAsync(Item("wf", "two", 2));

        var first = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf", "owner-a", Now, TimeSpan.FromSeconds(10)));
        var competing = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf", "owner-b", Now.AddSeconds(1), TimeSpan.FromSeconds(10)));
        Assert.NotNull(first);
        Assert.Null(competing);
        var renewedResult = await queue.RenewClaimAsync(first!, Now.AddSeconds(2), TimeSpan.FromMinutes(1));
        var renewed = Assert.IsType<RuntimeSchedulerWorkClaim>(renewedResult.Claim);
        Assert.True(renewed.Revision > first.Revision);
        Assert.Equal(first.FencingToken, renewed.FencingToken);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, (await queue.CompleteClaimAsync(first)).Status);

        var consumed = await queue.ConsumeClaimedAsync(ConsumedSchedulerWorkItem.FromClaim(renewed));
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, consumed.Status);
        Assert.Equal("two", (await queue.DequeueAsync("wf"))!.WorkItemId);
    }

    [Fact]
    public async Task Sqlite_consumption_survives_a_renewal_between_read_and_atomic_delete()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        connection.Schema.Apply(unit);
        var accessor = ScopedAccessor();
        IWorkflowSchedulerWorkQueue queue = new GroundworkV2WorkflowSchedulerWorkQueue(
            new DirectSessionSource(connection),
            accessor);
        await queue.EnqueueAsync(Item("wf-race", "work-1", 1));
        var claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await queue.ClaimAsync(
            new RuntimeSchedulerWorkClaimRequest("wf-race", "owner-a", Now, TimeSpan.FromSeconds(10))));
        RuntimeSchedulerWorkClaim? renewed = null;
        var consumingQueue = new GroundworkV2WorkflowSchedulerWorkQueue(
            new DirectSessionSource(
                connection,
                beforeFencedDelete: () =>
                {
                    var renewal = queue.RenewClaimAsync(
                            claim,
                            Now.AddSeconds(2),
                            TimeSpan.FromMinutes(1))
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                    renewed = Assert.IsType<RuntimeSchedulerWorkClaim>(renewal.Claim);
                }),
            accessor);

        var result = await consumingQueue.ConsumeClaimedAsync(ConsumedSchedulerWorkItem.FromClaim(claim));

        Assert.NotNull(renewed);
        Assert.Equal(claim.FencingToken, renewed!.FencingToken);
        Assert.True(renewed.Revision > claim.Revision);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, result.Status);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf-race"))).Items);
    }

    [Fact]
    public async Task Global_and_across_scope_access_fail_closed_before_provider_reads()
    {
        var source = new RecordingSource();
        var global = new GroundworkV2WorkflowSchedulerWorkQueue(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Global));
        var across = new GroundworkV2WorkflowSchedulerWorkQueue(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("test"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => global.ListAsync(new RuntimeSchedulerWorkQuery("wf")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => across.ListAsync(new RuntimeSchedulerWorkQuery("wf")).AsTask());
        Assert.Equal(0, source.OpenCount);
    }

    [Fact]
    public void Claim_transitions_require_evidenced_atomic_compare_and_delete()
    {
        var unsupported = new GroundworkV2WorkflowSchedulerWorkQueue(
            new RecordingSource(),
            ScopedAccessor());
        var supported = new GroundworkV2WorkflowSchedulerWorkQueue(
            new RecordingSource([BatchWriteCapabilities.CompareAndDeleteDescriptor]),
            ScopedAccessor());

        Assert.False(unsupported.SupportsClaimTransitions);
        Assert.True(supported.SupportsClaimTransitions);
    }

    [Fact]
    public async Task Sqlite_enqueue_list_dequeue_preserves_fifo_order_per_workflow_execution()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());

        await queue.EnqueueAsync(Item("wfexec-1", "work-1", 1));
        await queue.EnqueueAsync(Item("wfexec-1", "work-2", 2));
        await queue.EnqueueAsync(Item("wfexec-1", "work-3", 3));
        await queue.EnqueueAsync(Item("wfexec-2", "work-9", 9));

        Assert.Equal(["work-1", "work-2", "work-3"],
            (await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Items.Select(item => item.WorkItemId));
        Assert.Equal("work-1", (await queue.DequeueAsync("wfexec-1"))!.WorkItemId);
        Assert.Equal("work-2", (await queue.DequeueAsync("wfexec-1"))!.WorkItemId);
        Assert.Equal("work-3", (await queue.DequeueAsync("wfexec-1"))!.WorkItemId);
        Assert.Null(await queue.DequeueAsync("wfexec-1"));
        Assert.Equal("work-9", (await queue.DequeueAsync("wfexec-2"))!.WorkItemId);
    }

    [Fact]
    public async Task Sqlite_list_returns_one_capped_cursor_page_in_deterministic_queue_order()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        await queue.EnqueueAsync(Item("wf", "work-1", 1));
        await queue.EnqueueAsync(Item("wf", "work-2", 2));
        await queue.EnqueueAsync(Item("wf", "work-3", 3));

        var first = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf", 2));
        var second = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf", 2, first.NextContinuationToken));
        Assert.Equal(["work-1", "work-2"], first.Items.Select(item => item.WorkItemId));
        Assert.Equal(["work-3"], second.Items.Select(item => item.WorkItemId));
        Assert.NotNull(first.NextContinuationToken);
        Assert.Null(second.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.ListAsync(new RuntimeSchedulerWorkQuery("wf", 501)).AsTask());
    }

    [Fact]
    public async Task Sqlite_enqueue_is_idempotent_per_workflow_execution_and_work_item_id()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        var first = Item("wf", "work-1", 1, "command-first");
        var duplicate = Item("wf", "work-1", 99, "command-duplicate");

        Assert.Equal("command-first", (await queue.EnqueueAsync(first)).CommandId);
        Assert.Equal("command-first", (await queue.EnqueueAsync(duplicate)).CommandId);
        var stored = Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf"))).Items);
        Assert.Equal("command-first", stored.CommandId);
    }

    [Fact]
    public async Task Sqlite_reenqueueing_a_non_head_item_keeps_queue_contents_and_order_unchanged()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        await queue.EnqueueAsync(Item("wf", "work-1", 1));
        await queue.EnqueueAsync(Item("wf", "work-2", 2));
        await queue.EnqueueAsync(Item("wf", "work-3", 3));

        var redelivered = await queue.EnqueueAsync(Item("wf", "work-2", 2, "redelivered-command"));
        var items = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf"));
        Assert.Equal(["work-1", "work-2", "work-3"], items.Items.Select(item => item.WorkItemId));
        Assert.Equal("command-2", redelivered.CommandId);
        Assert.Equal("command-2", items.Items.ElementAt(1).CommandId);
    }

    [Fact]
    public async Task Sqlite_delete_removes_a_work_item_id_beyond_the_portable_document_limit()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        var workItemId = new string('x', ElsaRuntimeV2StorageManifest.IdMaximumLength);
        await queue.EnqueueAsync(Item("wf", workItemId, 1));

        Assert.True(await queue.DeleteAsync("wf", workItemId));
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf"))).Items);
        Assert.False(await queue.DeleteAsync("wf", workItemId));
    }

    [Fact]
    public async Task Sqlite_enqueue_physical_identity_collision_fails_closed()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        connection.Schema.Apply(unit);
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        var victim = Item("wf-victim", new string('x', ElsaRuntimeV2StorageManifest.IdMaximumLength), 1);
        await queue.EnqueueAsync(victim);
        var physicalId = ReadPhysicalId(source, unit);
        InstallForeignRow(source, unit, physicalId, Item("wf-foreign", "foreign", 2));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => queue.EnqueueAsync(victim).AsTask());
        Assert.Contains("physical identity collision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_delete_physical_identity_collision_fails_closed()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        connection.Schema.Apply(unit);
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        var victim = Item("wf-victim", new string('x', ElsaRuntimeV2StorageManifest.IdMaximumLength), 1);
        await queue.EnqueueAsync(victim);
        var physicalId = ReadPhysicalId(source, unit);
        InstallForeignRow(source, unit, physicalId, Item("wf-foreign", "foreign", 2));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.DeleteAsync(victim.WorkflowExecutionId, victim.WorkItemId).AsTask());
        Assert.Contains("physical identity collision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_queue_survives_a_real_provider_connection_restart()
    {
        await using var runtime = NativeProviderRuntime.Create();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        using (var connection = runtime.OpenConnection())
        {
            connection.Schema.Apply(unit);
            var queue = new GroundworkV2WorkflowSchedulerWorkQueue(new DirectSessionSource(connection), ScopedAccessor());
            await queue.EnqueueAsync(Item("wf", "work-1", 1));
            await queue.EnqueueAsync(Item("wf", "work-2", 2));
        }

        using (var reopened = runtime.OpenConnection())
        {
            var queue = new GroundworkV2WorkflowSchedulerWorkQueue(new DirectSessionSource(reopened), ScopedAccessor());
            var recovered = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf"));
            Assert.Equal(["work-1", "work-2"], recovered.Items.Select(item => item.WorkItemId));
            Assert.Equal("command-1", recovered.Items[0].CommandId);
            Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, recovered.Items[0].CommandKind);
            Assert.Equal(Now, recovered.Items[0].EnqueuedAt);
            Assert.Equal(1, recovered.Items[0].Sequence);
        }
    }

    [Fact]
    public async Task Sqlite_pending_workflow_ids_are_distinct_ordered_and_empty_after_dequeue()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        Assert.Empty(await queue.ListPendingWorkflowExecutionIdsAsync(10));
        await queue.EnqueueAsync(Item("wf-b", "work-1", 1));
        await queue.EnqueueAsync(Item("wf-b", "work-2", 2));
        await queue.EnqueueAsync(Item("wf-a", "work-3", 3));
        Assert.Equal(["wf-a", "wf-b"], await queue.ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Equal(["wf-a"], await queue.ListPendingWorkflowExecutionIdsAsync(1));
        await queue.DequeueAsync("wf-a");
        Assert.Equal(["wf-b"], await queue.ListPendingWorkflowExecutionIdsAsync(10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queue.ListPendingWorkflowExecutionIdsAsync(0).AsTask());
    }

    [Fact]
    public async Task Sqlite_pending_workflow_ids_are_deduplicated_before_the_limit_with_same_timestamp_ties()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        await queue.EnqueueAsync(Item("wf-a", "work-1", 1, recordedAt: Now));
        await queue.EnqueueAsync(Item("wf-a", "work-2", 2, recordedAt: Now));
        await queue.EnqueueAsync(Item("wf-a", "work-3", 3, recordedAt: Now));
        await queue.EnqueueAsync(Item("wf-b", "work-4", 4, recordedAt: Now));

        Assert.Equal(["wf-a", "wf-b"], await queue.ListPendingWorkflowExecutionIdsAsync(2));
        Assert.Equal(["wf-a", "wf-b"], await queue.ListPendingWorkflowExecutionIdsAsync(2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queue.ListPendingWorkflowExecutionIdsAsync(501).AsTask());
    }

    [Fact]
    public async Task Sqlite_pending_workflow_ids_use_one_bounded_ordered_provider_query()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var requests = new List<QueryRequest>();
        var source = new DirectSessionSource(connection, requests);
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        connection.Schema.Apply(unit);
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        await queue.EnqueueAsync(Item("wf-a", "work-1", 1));
        await queue.EnqueueAsync(Item("wf-a", "work-2", 2));
        await queue.EnqueueAsync(Item("wf-b", "work-3", 3));
        requests.Clear();

        Assert.Equal(["wf-a", "wf-b"], await queue.ListPendingWorkflowExecutionIdsAsync(2));
        var query = Assert.Single(requests);
        Assert.Equal(2, query.Paging.Limit);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, query.LatestPerKey!.Key.Name);
        Assert.Equal(ElsaRuntimeV2StorageManifest.SchedulerWorkRecordedAtField, query.LatestPerKey.Timestamp.Name);
        Assert.Collection(
            query.Order,
            first => Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, first.Column.Name),
            second => Assert.Equal(ElsaRuntimeV2StorageManifest.SchedulerWorkRecordedAtField, second.Column.Name),
            third => Assert.Equal(ElsaRuntimeV2StorageManifest.SchedulerWorkOrderKeyField, third.Column.Name));
        Assert.Null(query.Paging.ContinuationToken);
    }

    [Fact]
    public async Task Sqlite_separator_characters_do_not_collide_in_composite_physical_ids()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        await queue.EnqueueAsync(Item("a:b", "c", 1));
        await queue.EnqueueAsync(Item("a", "b:c", 2));
        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("a:b"))).Items);
        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("a"))).Items);
    }

    [Fact]
    public async Task Sqlite_concurrent_claim_grants_one_owner_and_keeps_later_work_behind_the_head()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection);
        var setupQueue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        await setupQueue.EnqueueAsync(Item("wf", "work-1", 1));
        await setupQueue.EnqueueAsync(Item("wf", "work-2", 2));

        var first = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        var second = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        var claims = await Task.WhenAll(
            Task.Run(async () => await first.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf", "owner-a", Now, TimeSpan.FromMinutes(1)))),
            Task.Run(async () => await second.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf", "owner-b", Now, TimeSpan.FromMinutes(1)))));
        var winner = Assert.Single(claims, claim => claim is not null);
        Assert.Equal("work-1", winner!.Item.WorkItemId);
        Assert.Null(await first.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf", "owner-c", Now.AddSeconds(1), TimeSpan.FromMinutes(1))));
        Assert.Equal(["work-1", "work-2"],
            (await first.ListAsync(new RuntimeSchedulerWorkQuery("wf"))).Items.Select(item => item.WorkItemId));
    }

    [Fact]
    public async Task Sqlite_expired_claim_is_reclaimed_with_a_higher_fence_and_stale_completion_cannot_remove_successor()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var source = new DirectSessionSource(connection);
        connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind));
        var queue = new GroundworkV2WorkflowSchedulerWorkQueue(source, ScopedAccessor());
        await queue.EnqueueAsync(Item("wf", "work-1", 1));
        var first = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf", "owner-a", Now, TimeSpan.FromSeconds(10)));
        var successor = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf", "owner-b", Now.AddSeconds(11), TimeSpan.FromMinutes(1)));
        Assert.NotNull(first);
        Assert.NotNull(successor);
        Assert.True(successor!.FencingToken > first!.FencingToken);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, (await queue.CompleteClaimAsync(first)).Status);
        Assert.Single((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf"))).Items);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, (await queue.CompleteClaimAsync(successor)).Status);
        Assert.Empty((await queue.ListAsync(new RuntimeSchedulerWorkQuery("wf"))).Items);
    }

    [Fact]
    public async Task Sqlite_release_delays_retry_and_completion_is_idempotent_across_real_recreation()
    {
        await using var runtime = NativeProviderRuntime.Create();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerWorkItemDocumentKind);
        RuntimeSchedulerWorkClaim claim;
        using (var connection = runtime.OpenConnection())
        {
            connection.Schema.Apply(unit);
            var queue = new GroundworkV2WorkflowSchedulerWorkQueue(new DirectSessionSource(connection), ScopedAccessor());
            await queue.EnqueueAsync(Item("wf", "work-1", 1));
            claim = Assert.IsType<RuntimeSchedulerWorkClaim>(await queue.ClaimAsync(
                new RuntimeSchedulerWorkClaimRequest("wf", "owner-a", Now, TimeSpan.FromMinutes(1))));
            Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
                (await queue.ReleaseClaimAsync(claim, Now.AddMinutes(2))).Status);
        }

        RuntimeSchedulerWorkClaim retried;
        using (var connection = runtime.OpenConnection())
        {
            var queue = new GroundworkV2WorkflowSchedulerWorkQueue(new DirectSessionSource(connection), ScopedAccessor());
            Assert.Null(await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf", "owner-b", Now.AddMinutes(1), TimeSpan.FromMinutes(1))));
            retried = Assert.IsType<RuntimeSchedulerWorkClaim>(await queue.ClaimAsync(
                new RuntimeSchedulerWorkClaimRequest("wf", "owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1))));
            Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, (await queue.CompleteClaimAsync(retried)).Status);
        }

        using (var connection = runtime.OpenConnection())
        {
            var queue = new GroundworkV2WorkflowSchedulerWorkQueue(new DirectSessionSource(connection), ScopedAccessor());
            Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.AlreadyApplied,
                (await queue.CompleteClaimAsync(retried)).Status);
        }
    }

    private static TestAccessContextAccessor ScopedAccessor(string scope = "tenant-a") =>
        new(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    private static string ReadPhysicalId(DirectSessionSource source, StorageUnit unit)
    {
        var row = source.Open(
                unit.Id.Value,
                StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Query(new QueryRequest(
                new TableId(unit.Name),
                Predicate.AlwaysTrue.Instance,
                [],
                Projection.All,
                Paging.Keyset(10)))
            .Rows.Single();
        return (string)row[ElsaRuntimeV2StorageManifest.IdField]!;
    }

    private static void InstallForeignRow(
        DirectSessionSource source,
        StorageUnit unit,
        string physicalId,
        RuntimeSchedulerWorkItem foreignItem)
    {
        var envelope = GroundworkV2SchedulerWorkStorageConventions.NewEnvelope(foreignItem);
        var values = GroundworkV2SchedulerWorkStorageConventions.Values(envelope).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        // Keep the foreign content/projections self-consistent while occupying the victim's
        // hashed physical key. This exercises alias collision validation rather than a stale
        // order/projection failure.
        values[ElsaRuntimeV2StorageManifest.IdField] = physicalId;
        var outcome = source.Open(
                unit.Id.Value,
                StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Upsert(new StorageValues(values), WriteOptions.Unconditional);
        Assert.Equal(WriteOutcomeStatus.Upserted, outcome.Status);
    }

    private static RuntimeSchedulerWorkItem Item(
        string workflowExecutionId,
        string workItemId,
        int sequence,
        string? commandId = null,
        DateTimeOffset? recordedAt = null)
    {
        using var payload = JsonDocument.Parse($"{{\"workItemId\":\"{workItemId}\"}}");
        return new RuntimeSchedulerWorkItem(
            workItemId,
            workflowExecutionId,
            commandId ?? $"command-{sequence}",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            $"envelope-{sequence}",
            $"idempotency-{workflowExecutionId}-{sequence}",
            Now,
            recordedAt ?? Now.AddMilliseconds(sequence),
            sequence,
            payload.RootElement.Clone());
    }

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource(
        IStorageProviderConnection connection,
        ICollection<QueryRequest>? queryRequests = null,
        Action? beforeFencedDelete = null) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new RecordingSession(
                connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access),
                queryRequests,
                beforeFencedDelete);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class RecordingSession(
        IStorageSession inner,
        ICollection<QueryRequest>? queryRequests,
        Action? beforeFencedDelete) : SynchronousStorageSessionTestDouble, IStorageSession, IConcurrencyStorageSession, ICompareAndDeleteStorageSession
    {
        private Action? beforeFencedDelete = beforeFencedDelete;

        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;
        public StoredEntry? Read(StorageKey key) => inner.Read(key);

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            queryRequests?.Add(request);
            return inner.Query(request, options);
        }

        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null)
        {
            BeforeFencedDelete();
            return inner.Delete(key, options);
        }

        public WriteOutcome CompareAndDelete(
            StorageKey key,
            IReadOnlyDictionary<string, object?> expectedValues,
            WriteOptions? options = null)
        {
            BeforeFencedDelete();
            return ((ICompareAndDeleteStorageSession)inner).CompareAndDelete(key, expectedValues, options);
        }

        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);

        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
            ((IConcurrencyStorageSession)inner).ConditionalUpsert(values, options);

        private void BeforeFencedDelete() => Interlocked.Exchange(ref beforeFencedDelete, null)?.Invoke();
    }

    private sealed class RecordingSource(
        IReadOnlyList<CapabilityDescriptor>? capabilities = null) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
    {
        public int OpenCount { get; private set; }
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            throw new InvalidOperationException();
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();
        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);
        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => capabilities ?? [];
    }

    private sealed class NativeProviderRuntime(string path) : IAsyncDisposable
    {
        public static NativeProviderRuntime Create() =>
            new(Path.Combine(Path.GetTempPath(), $"elsa-runtime-scheduler-v2-{Guid.NewGuid():N}.db"));
        public IStorageProviderConnection OpenConnection() => new SqliteProviderFactory().Create($"Data Source={path}");
        public ValueTask DisposeAsync()
        {
            foreach (var candidate in new[]
                     {
                         path,
                         $"{path}-shm",
                         $"{path}-wal",
                         $"{path}-journal",
                         $"{path}.schema.lock"
                     })
                if (File.Exists(candidate))
                    File.Delete(candidate);
            return ValueTask.CompletedTask;
        }
    }
}
