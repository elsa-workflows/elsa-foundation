using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowTestScopeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddHours(1);

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Create_find_and_equivalent_replay_are_idempotent_while_conflicting_context_is_rejected(
        string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-create", Now.AddHours(1));

        var created = await stores.Scope.CreateAsync(scope, Now);
        var replay = await stores.Scope.CreateAsync(scope, Now.AddMinutes(1));
        var found = Assert.IsType<WorkflowTestScopeRecord>(await stores.Scope.FindAsync(scope.ScopeId));

        Assert.Equal(WorkflowTestScopeState.Open, created.State);
        Assert.Equal(created, replay);
        Assert.Equal(created, found);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stores.Scope.CreateAsync(Scope(scope.ScopeId, scope.ExpiresAt.AddMinutes(1)), Now).AsTask());
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Expiry_and_closing_queries_return_only_the_requested_lifecycle_candidates(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var expired = Scope("scope-expired", Now.AddMinutes(10));
        var future = Scope("scope-future", Now.AddHours(2));
        var closing = Scope("scope-closing", Now.AddHours(3));
        await stores.Scope.CreateAsync(expired, Now);
        await stores.Scope.CreateAsync(future, Now);
        await stores.Scope.CreateAsync(closing, Now);
        await stores.Scope.CloseAsync(
            new WorkflowTestScopeCloseRequest(
                closing.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(20)));

        var resumable = await stores.Scope.QueryAsync(
            new WorkflowTestScopePageQuery(Now.AddMinutes(20), 10));
        var closingOnly = await stores.Scope.QueryAsync(
            new WorkflowTestScopePageQuery(
                Now.AddMinutes(20),
                10,
                WorkflowTestScopeState.Closing));

        Assert.Equal(
            new[] { closing.ScopeId, expired.ScopeId },
            resumable.Items.Select(item => item.Scope.ScopeId));
        Assert.Equal(closing.ScopeId, Assert.Single(closingOnly.Items).Scope.ScopeId);
        Assert.DoesNotContain(resumable.Items, item => item.Scope.ScopeId == future.ScopeId);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Expired_open_scope_is_not_starved_by_unexpired_records(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        for (var i = 0; i < WorkflowDispatchQuery.MaximumTake + 5; i++)
        {
            var future = Scope($"future-{i:D3}", Now.AddDays(2).AddMinutes(i));
            await stores.Scope.CreateAsync(future, Now);
        }
        var expired = Scope("expired-target", Now.AddMinutes(1));
        await stores.Scope.CreateAsync(expired, Now);

        var page = await stores.Scope.QueryAsync(
            new WorkflowTestScopePageQuery(Now.AddMinutes(2), 1));

        Assert.Equal(expired.ScopeId, Assert.Single(page.Items).Scope.ScopeId);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Closing_scope_pagination_reaches_beyond_one_provider_page(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < WorkflowDispatchQuery.MaximumTake + 5; i++)
        {
            var scope = Scope($"closing-{i:D3}", Now.AddHours(2));
            expected.Add(scope.ScopeId);
            await stores.Scope.CreateAsync(scope, Now);
            await stores.Scope.CloseAsync(new WorkflowTestScopeCloseRequest(
                scope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(1)));
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        do
        {
            var page = await stores.Scope.QueryAsync(new WorkflowTestScopePageQuery(
                Now.AddMinutes(2),
                25,
                WorkflowTestScopeState.Closing,
                continuation));
            found.UnionWith(page.Items.Select(item => item.Scope.ScopeId));
            continuation = page.ContinuationToken;
        } while (continuation is not null);

        Assert.True(expected.SetEquals(found));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Scope_reads_fail_closed_outside_the_bound_tenant_while_allowing_distinct_runtime_partition(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope(
            "scope-context",
            Now.AddHours(1),
            tenantId: GroundworkTestAccess.DefaultScopeValue,
            partition: "partition-eu");
        await stores.Scope.CreateAsync(scope, Now);
        Assert.Equal("partition-eu", (await stores.Scope.FindAsync(scope.ScopeId))!.Scope.Partition.Value);
        var foreign = CreateStores(fixture, GroundworkTestAccess.AccessContext("foreign-tenant"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            foreign.Scope.FindAsync(scope.ScopeId).AsTask());

        Assert.DoesNotContain(scope.ScopeId, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Closed_scope_cancels_pending_child_before_admission(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-closed-admission", Now.AddHours(1));
        await stores.Scope.CreateAsync(scope, Now);
        var close = await stores.Scope.CloseAsync(
            new WorkflowTestScopeCloseRequest(
                scope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(1)));
        Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted, close.Disposition);
        await stores.Scope.CompleteAsync(scope.ScopeId, Now.AddMinutes(2));
        var pending = Dispatch("parent-closed", "activity-1", scope);
        await stores.Dispatch.SaveAsync(pending);

        var admission = await stores.Dispatch.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(3));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, admission.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, admission.Record.Status);
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(admission.Record));
        Assert.Equal(
            WorkflowDispatchLifecycle.ScopeCancelledBeforeAdmissionState,
            admission.Record.Metadata[WorkflowDispatchLifecycle.CancellationStateMetadataKey]);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Closed_scope_does_not_take_cancellation_authority_over_waited_child(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-closed-waited", Now.AddHours(1));
        await stores.Scope.CreateAsync(scope, Now);
        await stores.Scope.CloseAsync(
            new WorkflowTestScopeCloseRequest(
                scope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(1)));
        var pending = Dispatch("parent-waited", "activity-1", scope, WorkflowDispatchMode.WaitForCompletion);
        await stores.Dispatch.SaveAsync(pending);

        var admission = await stores.Dispatch.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(2));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admission.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Started, admission.Record.Status);
        Assert.False(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(admission.Record));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Exact_scope_dispatch_query_excludes_other_scopes_and_unscoped_dispatches(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var firstScope = Scope("scope-query-a", Now.AddHours(1));
        var secondScope = Scope("scope-query-b", Now.AddHours(1));
        var first = Dispatch("parent-query-a", "activity-1", firstScope);
        var second = Dispatch("parent-query-b", "activity-1", secondScope);
        var unscoped = GroundworkWorkflowDispatchStoreTests.Pending("parent-query-none", "activity-1");
        await stores.Dispatch.SaveAsync(first);
        await stores.Dispatch.SaveAsync(second);
        await stores.Dispatch.SaveAsync(unscoped);

        var found = await stores.Dispatch.QueryAsync(
            new WorkflowDispatchQuery(testScopeId: firstScope.ScopeId));

        Assert.Equal(first.DispatchId, Assert.Single(found).DispatchId);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Started_cleanup_persists_marker_and_outbox_and_replay_converges_after_store_recreation(
        string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-started-cleanup", Now.AddHours(1));
        await stores.Scope.CreateAsync(scope, Now);
        var pending = Dispatch("parent-started", "activity-1", scope);
        await stores.Dispatch.SaveAsync(pending);
        var admitted = await stores.Dispatch.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(1));
        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admitted.Disposition);
        await stores.Scope.CloseAsync(
            new WorkflowTestScopeCloseRequest(
                scope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(2)));
        var intent = CancelIntent(admitted.Record, Now.AddMinutes(2));
        var intents = new Dictionary<string, RuntimePostCommitIntent>(StringComparer.Ordinal)
        {
            [pending.DispatchId] = intent
        };

        var first = await stores.Cleanup.CleanupAsync(scope, Now.AddMinutes(2), 10, intents);
        var identity = new WorkflowDispatchIdentity(
            pending.ParentWorkflowExecutionId,
            pending.ParentActivityExecutionId);
        var outboxId = identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");
        var marked = Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatch.FindAsync(pending.DispatchId));
        var outbox = Assert.IsType<RuntimePostCommitOutboxItem>(await stores.Outbox.FindAsync(outboxId));

        Assert.Equal(1, first.CancellationQueued);
        Assert.True(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(marked));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, outbox.Status);
        Assert.Equal(intent.IntentId, outbox.Intent.IntentId);
        Assert.Equal(intent.IdempotencyKey, outbox.Intent.IdempotencyKey);

        var recreated = CreateStores(fixture);
        var replay = await recreated.Cleanup.CleanupAsync(scope, Now.AddMinutes(2), 10, intents);
        var replayedDispatch = Assert.IsType<WorkflowDispatchRecord>(
            await recreated.Dispatch.FindAsync(pending.DispatchId));
        var replayedOutbox = Assert.IsType<RuntimePostCommitOutboxItem>(
            await recreated.Outbox.FindAsync(outboxId));

        Assert.Equal(0, replay.CancellationQueued);
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(marked, replayedDispatch));
        Assert.Equal(outbox.OutboxItemId, replayedOutbox.OutboxItemId);
        Assert.Equal(outbox.Intent.IntentId, replayedOutbox.Intent.IntentId);
        Assert.Equal(outbox.RecordedAt, replayedOutbox.RecordedAt);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Cleaner_replay_at_later_time_reuses_the_stable_close_timestamp(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-later-cleaner-replay", Now.AddHours(1));
        await stores.Scope.CreateAsync(scope, Now);
        var pending = Dispatch("parent-later-replay", "activity-1", scope);
        await stores.Dispatch.SaveAsync(pending);
        await stores.Dispatch.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(1));
        var firstCleaner = new WorkflowTestScopeCleaner(stores.Scope, stores.Cleanup, stores.Dispatch);

        var first = await firstCleaner.CloseAsync(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            Now.AddMinutes(2));
        var recreated = CreateStores(fixture);
        var replayCleaner = new WorkflowTestScopeCleaner(recreated.Scope, recreated.Cleanup, recreated.Dispatch);
        var replay = await replayCleaner.CloseAsync(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            Now.AddMinutes(20));

        Assert.Equal(1, first.CancellationQueued);
        Assert.Equal(0, replay.CancellationQueued);
        var identity = new WorkflowDispatchIdentity(
            pending.ParentWorkflowExecutionId,
            pending.ParentActivityExecutionId);
        var outbox = Assert.IsType<RuntimePostCommitOutboxItem>(await recreated.Outbox.FindAsync(
            identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}")));
        Assert.Equal(Now.AddMinutes(2), outbox.RecordedAt);
        Assert.Equal(Now.AddMinutes(2), outbox.Intent.RecordedAt);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Concurrent_cleaners_and_response_loss_replay_cancel_pending_child_once(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-concurrent-cleaners", Now.AddHours(1));
        var pending = Dispatch("parent-concurrent-cleaners", "activity-1", scope);
        await stores.Scope.CreateAsync(scope, Now);
        await stores.Dispatch.SaveAsync(pending);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            var cleaner = new WorkflowTestScopeCleaner(stores.Scope, stores.Cleanup, stores.Dispatch);
            return await cleaner.CloseAsync(
                scope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(2));
        }));

        Assert.Equal(1, results.Sum(result => result.CancelledBeforeAdmission));
        var cancelled = Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatch.FindAsync(pending.DispatchId));
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(cancelled));

        var recreated = CreateStores(fixture);
        var replay = await new WorkflowTestScopeCleaner(recreated.Scope, recreated.Cleanup, recreated.Dispatch)
            .CloseAsync(
                scope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(20));

        Assert.Equal(0, replay.CancelledBeforeAdmission);
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(
            cancelled,
            Assert.IsType<WorkflowDispatchRecord>(await recreated.Dispatch.FindAsync(pending.DispatchId))));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Started_cleanup_rolls_back_marker_when_outbox_staging_fails_and_retry_converges(
        string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-cleanup-rollback", Now.AddHours(1));
        await stores.Scope.CreateAsync(scope, Now);
        var pending = Dispatch("parent-rollback", "activity-1", scope);
        await stores.Dispatch.SaveAsync(pending);
        var admitted = await stores.Dispatch.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(1));
        await stores.Scope.CloseAsync(
            new WorkflowTestScopeCloseRequest(
                scope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(2)));
        var intent = CancelIntent(admitted.Record, Now.AddMinutes(2));
        var intents = new Dictionary<string, RuntimePostCommitIntent>(StringComparer.Ordinal)
        {
            [pending.DispatchId] = intent
        };
        var failingStores = CreateStores(
            new OutboxSaveFailingDocumentStore(fixture.DocumentStore),
            fixture.BoundedDocumentStore);

        await Assert.ThrowsAsync<InjectedOutboxSaveException>(() =>
            failingStores.Cleanup.CleanupAsync(scope, Now.AddMinutes(2), 10, intents).AsTask());

        var identity = new WorkflowDispatchIdentity(
            pending.ParentWorkflowExecutionId,
            pending.ParentActivityExecutionId);
        var outboxId = identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");
        var rolledBack = Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatch.FindAsync(pending.DispatchId));
        Assert.False(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(rolledBack));
        Assert.Null(await stores.Outbox.FindAsync(outboxId));

        var retry = await stores.Cleanup.CleanupAsync(scope, Now.AddMinutes(2), 10, intents);

        Assert.Equal(1, retry.CancellationQueued);
        Assert.True(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(
            Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatch.FindAsync(pending.DispatchId))));
        Assert.NotNull(await stores.Outbox.FindAsync(outboxId));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Terminal_prefix_does_not_starve_a_live_detached_cleanup_page(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-terminal-prefix", Now.AddHours(1));
        await stores.Scope.CreateAsync(scope, Now);
        for (var i = 0; i < WorkflowDispatchQuery.MaximumTake + 1; i++)
        {
            var pending = Dispatch($"a-terminal-{i:D3}", "activity-1", scope);
            await stores.Dispatch.SaveAsync(pending);
            await stores.Dispatch.SaveAsync(
                pending.TransitionTo(WorkflowDispatchStatus.Completed, Now.AddMinutes(1)));
        }
        var live = Dispatch("z-live", "activity-1", scope);
        await stores.Dispatch.SaveAsync(live);
        await stores.Scope.CloseAsync(
            new WorkflowTestScopeCloseRequest(
                scope.ScopeId,
                WorkflowTestScopeCloseReason.ExplicitTeardown,
                Now.AddMinutes(2)));

        var result = await stores.Cleanup.CleanupAsync(
            scope,
            Now.AddMinutes(2),
            pageSize: 1,
            cancellationIntents: new Dictionary<string, RuntimePostCommitIntent>());

        Assert.Equal(1, result.Inspected);
        Assert.Equal(1, result.CancelledBeforeAdmission);
        Assert.Equal(0, result.RemainingLive);
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(
            Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatch.FindAsync(live.DispatchId))));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Marked_started_prefix_does_not_starve_a_later_pending_dispatch(string provider)
    {
        await using var fixture = CreateFixture(provider);
        var stores = CreateStores(fixture);
        var scope = Scope("scope-marked-prefix", Now.AddHours(1));
        await stores.Scope.CreateAsync(scope, Now);
        for (var i = 0; i < WorkflowDispatchQuery.MaximumTake + 1; i++)
        {
            var pending = Dispatch($"a-marked-{i:D3}", "activity-1", scope);
            await stores.Dispatch.SaveAsync(pending);
            var admitted = await stores.Dispatch.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(1));
            Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admitted.Disposition);
            await stores.Dispatch.SaveAsync(
                WorkflowDispatchLifecycle.MarkTestScopeCancellationRequested(admitted.Record, Now.AddMinutes(2)));
        }

        var live = Dispatch("z-live", "activity-1", scope);
        await stores.Dispatch.SaveAsync(live);
        await stores.Scope.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            Now.AddMinutes(2)));

        var result = await stores.Cleanup.CleanupAsync(
            scope,
            Now.AddMinutes(2),
            pageSize: 1,
            cancellationIntents: new Dictionary<string, RuntimePostCommitIntent>());

        Assert.Equal(1, result.Inspected);
        Assert.Equal(1, result.CancelledBeforeAdmission);
        Assert.Equal(WorkflowDispatchQuery.MaximumTake + 1, result.RemainingLive);
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(
            Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatch.FindAsync(live.DispatchId))));
    }

    private static Stores CreateStores(
        GroundworkDocumentStoreFixture fixture,
        IPersistenceAccessContextAccessor? accessContextAccessor = null)
        => CreateStores(fixture.DocumentStore, fixture.BoundedDocumentStore, accessContextAccessor);

    private static GroundworkDocumentStoreFixture CreateFixture(string provider)
    {
        var manifest = new RuntimeGroundworkStorageManifestSource()
            .CreateDeclarationAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult()
            .Manifest;
        return GroundworkDocumentStoreFixture.Create(provider, manifest);
    }

    private static Stores CreateStores(
        IDocumentStore documentStore,
        IBoundedDocumentStore boundedDocumentStore,
        IPersistenceAccessContextAccessor? accessContextAccessor = null)
    {
        var access = accessContextAccessor ?? GroundworkTestAccess.DefaultAccessContextAccessor;
        var scope = new GroundworkWorkflowTestScopeStore(
            documentStore,
            GroundworkTestSerialization.Serializer,
            access,
            boundedDocumentStore);
        var dispatch = new GroundworkWorkflowDispatchStore(
            documentStore,
            GroundworkTestSerialization.Serializer,
            access,
            boundedDocumentStore,
            scope);
        var outbox = new GroundworkRuntimePostCommitOutboxStore(
            documentStore,
            GroundworkTestSerialization.Serializer,
            boundedDocumentStore,
            access);
        return new Stores(
            scope,
            dispatch,
            outbox,
            new GroundworkTestScopeCleanupStore(scope, dispatch, outbox));
    }

    private static WorkflowTestScope Scope(
        string scopeId,
        DateTimeOffset expiresAt,
        string? tenantId = null,
        string partition = WorkflowExecutionPartition.DefaultValue) => new(
        scopeId,
        expiresAt,
        tenantId,
        new WorkflowExecutionPartition(partition));

    private static WorkflowDispatchRecord Dispatch(
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        WorkflowTestScope scope,
        WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget)
    {
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, parentActivityExecutionId);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity(
                $"artifact-{parentActivityExecutionId}",
                "definition-child",
                "version-child",
                "1",
                $"hash-{parentActivityExecutionId}"),
            new WorkflowExecutableSourceProvenance(
                $"source-{parentActivityExecutionId}",
                "WorkflowDefinitionVersion",
                "version-child",
                "1",
                "definition-child",
                "version-child",
                "1",
                "publication-child",
                "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            correlationId: null,
            scope.TenantId,
            scope.Partition,
            WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parentWorkflowExecutionId, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            Now,
            Now,
            new Dictionary<string, string>(),
            scope);
    }

    private static RuntimePostCommitIntent CancelIntent(
        WorkflowDispatchRecord dispatch,
        DateTimeOffset recordedAt)
    {
        var identity = new WorkflowDispatchIdentity(
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId);
        return new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            dispatch.ParentWorkflowExecutionId,
            DispatchWorkflowConstants.CancelChildIntentKind,
            recordedAt,
            dispatch.ParentActivityExecutionId,
            identity.ChildCancelIdempotencyKey,
            JsonSerializer.SerializeToElement(
                new WorkflowDispatchChildCancelPayload(
                    dispatch.DispatchId,
                    dispatch.ParentWorkflowExecutionId,
                    dispatch.ParentActivityExecutionId,
                    dispatch.ChildWorkflowExecutionId),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
            });
    }

    private sealed record Stores(
        GroundworkWorkflowTestScopeStore Scope,
        GroundworkWorkflowDispatchStore Dispatch,
        GroundworkRuntimePostCommitOutboxStore Outbox,
        GroundworkTestScopeCleanupStore Cleanup);

    private sealed class OutboxSaveFailingDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public DocumentStoreAccess Access => inner.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) => inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.DeleteAsync(request, cancellationToken);

        #pragma warning disable GW0004 // Required IDocumentStore compatibility members; the portable query surface is retired but still abstract.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.AnyAsync(query, cancellationToken);
        #pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new OutboxSaveFailingUnitOfWork(await inner.BeginAsync(scope, cancellationToken));
    }

    private sealed class OutboxSaveFailingUnitOfWork(IDocumentUnitOfWork inner) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.DocumentKind == ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind)
                throw new InjectedOutboxSaveException();
            return inner.SaveAsync(request, cancellationToken);
        }

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) => inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => inner.DeleteAsync(request, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class InjectedOutboxSaveException : Exception;
}
