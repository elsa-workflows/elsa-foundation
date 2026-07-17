using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowTestScopeStoreTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = CreatedAt.AddHours(1);
    private static readonly DateTimeOffset ClosedAt = CreatedAt.AddMinutes(10);

    [Fact]
    public async Task Create_is_idempotent_for_equal_context_and_rejects_conflicts()
    {
        var stores = NewStores();
        var scope = NewScope();

        var created = await stores.Scopes.CreateAsync(scope, CreatedAt);
        var replayed = await stores.Scopes.CreateAsync(NewScope(), CreatedAt.AddMinutes(1));

        Assert.Same(created, replayed);
        Assert.Same(created, await stores.Scopes.FindAsync(scope.ScopeId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stores.Scopes.CreateAsync(
            NewScope(tenantId: "tenant-other"),
            CreatedAt.AddMinutes(1)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => stores.Scopes.CreateAsync(
            NewScope(partition: "partition-other"),
            CreatedAt.AddMinutes(1)).AsTask());
    }

    [Fact]
    public async Task Default_query_includes_open_scope_at_expiry_equality()
    {
        var stores = NewStores();
        await stores.Scopes.CreateAsync(NewScope("scope-a"), CreatedAt);
        await stores.Scopes.CreateAsync(NewScope("scope-b", ExpiresAt.AddMinutes(1)), CreatedAt);

        Assert.Empty((await stores.Scopes.QueryAsync(
            new WorkflowTestScopePageQuery(ExpiresAt.AddTicks(-1), 10))).Items);

        var first = await stores.Scopes.QueryAsync(new WorkflowTestScopePageQuery(ExpiresAt, 1));

        Assert.Equal("scope-a", Assert.Single(first.Items).Scope.ScopeId);
        Assert.Null(first.ContinuationToken);
    }

    [Fact]
    public async Task Close_and_completion_are_idempotent_and_AssertOpen_rejects_nonopen_context()
    {
        var stores = NewStores();
        var scope = NewScope();
        await stores.Scopes.CreateAsync(scope, CreatedAt);

        await stores.Scopes.AssertOpenAsync(scope, ExpiresAt.AddTicks(-1));
        await Assert.ThrowsAsync<TestScopeAdmissionException>(() => stores.Scopes.AssertOpenAsync(
            NewScope(tenantId: "tenant-other"),
            CreatedAt.AddMinutes(1)).AsTask());
        await Assert.ThrowsAsync<TestScopeAdmissionException>(() => stores.Scopes.AssertOpenAsync(
            scope,
            ExpiresAt).AsTask());

        var request = new WorkflowTestScopeCloseRequest(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            ClosedAt);
        var accepted = await stores.Scopes.CloseAsync(request);
        var duplicate = await stores.Scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.Expired,
            ExpiresAt));

        Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted, accepted.Disposition);
        Assert.Equal(WorkflowTestScopeCloseDisposition.AlreadyClosing, duplicate.Disposition);
        Assert.Equal(WorkflowTestScopeCloseReason.ExplicitTeardown, duplicate.Record!.CloseReason);
        await Assert.ThrowsAsync<TestScopeAdmissionException>(() => stores.Scopes.AssertOpenAsync(
            scope,
            ClosedAt).AsTask());

        var completed = await stores.Scopes.CompleteAsync(scope.ScopeId, ClosedAt.AddMinutes(1));
        var completionReplay = await stores.Scopes.CompleteAsync(scope.ScopeId, ClosedAt.AddMinutes(2));
        var closeAfterCompletion = await stores.Scopes.CloseAsync(request);

        Assert.Equal(WorkflowTestScopeState.Closed, completed.State);
        Assert.Same(completed, completionReplay);
        Assert.Equal(WorkflowTestScopeCloseDisposition.AlreadyClosed, closeAfterCompletion.Disposition);
        Assert.Same(completed, closeAfterCompletion.Record);
    }

    [Fact]
    public async Task Pending_detached_cleanup_cancels_before_admission_and_can_complete_scope()
    {
        var stores = NewStores();
        var scope = NewScope();
        var pending = NewDispatch("pending", scope);
        await stores.Scopes.CreateAsync(scope, CreatedAt);
        await stores.Dispatches.SaveAsync(pending);
        await CloseAsync(stores.Scopes, scope);

        var result = await stores.Scopes.CleanupAsync(scope, ClosedAt, 10, EmptyIntents);
        var persisted = Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatches.FindAsync(pending.DispatchId));
        var admission = await stores.Dispatches.TryAdmitAsync(pending.DispatchId, ClosedAt.AddMinutes(1));

        Assert.Equal(1, result.Inspected);
        Assert.Equal(1, result.CancelledBeforeAdmission);
        Assert.Equal(0, result.CancellationQueued);
        Assert.Equal(0, result.RemainingLive);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, persisted.Status);
        Assert.Equal(
            WorkflowDispatchLifecycle.ScopeCancelledBeforeAdmissionState,
            persisted.Metadata[WorkflowDispatchLifecycle.CancellationStateMetadataKey]);
        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, admission.Disposition);

        var completed = await stores.Scopes.CompleteAsync(scope.ScopeId, ClosedAt.AddMinutes(1));
        Assert.Equal(WorkflowTestScopeState.Closed, completed.State);
    }

    [Fact]
    public async Task Closing_scope_wins_before_detached_admission_without_waiting_for_cleanup()
    {
        var stores = NewStores();
        var scope = NewScope("scope-close-wins");
        var pending = NewDispatch("close-wins", scope);
        await stores.Scopes.CreateAsync(scope, CreatedAt);
        await stores.Dispatches.SaveAsync(pending);
        await CloseAsync(stores.Scopes, scope);

        var admission = await stores.Dispatches.TryAdmitAsync(pending.DispatchId, ClosedAt.AddMinutes(1));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, admission.Disposition);
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(admission.Record));
    }

    [Fact]
    public async Task Closing_scope_does_not_take_direct_admission_authority_over_waited_child()
    {
        var stores = NewStores();
        var scope = NewScope("scope-waited-admission");
        var pending = NewDispatch("waited-admission", scope, WorkflowDispatchMode.WaitForCompletion);
        await stores.Scopes.CreateAsync(scope, CreatedAt);
        await stores.Dispatches.SaveAsync(pending);
        await CloseAsync(stores.Scopes, scope);

        var admission = await stores.Dispatches.TryAdmitAsync(pending.DispatchId, ClosedAt.AddMinutes(1));

        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admission.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Started, admission.Record.Status);
    }

    [Fact]
    public async Task Started_detached_cleanup_marks_dispatch_and_commits_deterministic_outbox_responsibility()
    {
        var stores = NewStores();
        var scope = NewScope();
        var pending = NewDispatch("started", scope);
        var identity = Identity(pending);
        var intent = CancellationIntent(pending, ClosedAt);
        await stores.Scopes.CreateAsync(scope, CreatedAt);
        await stores.Dispatches.SaveAsync(pending);
        await stores.Dispatches.TryAdmitAsync(pending.DispatchId, CreatedAt.AddMinutes(1));
        await CloseAsync(stores.Scopes, scope);

        var result = await stores.Scopes.CleanupAsync(
            scope,
            ClosedAt,
            10,
            new Dictionary<string, RuntimePostCommitIntent> { [pending.DispatchId] = intent });

        var persisted = Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatches.FindAsync(pending.DispatchId));
        var outboxId = identity.ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");
        var outbox = Assert.IsType<RuntimePostCommitOutboxItem>(await stores.Outbox.FindAsync(outboxId));

        Assert.Equal(1, result.Inspected);
        Assert.Equal(1, result.CancellationQueued);
        Assert.Equal(1, result.RemainingLive);
        Assert.Equal(WorkflowDispatchStatus.Started, persisted.Status);
        Assert.True(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(persisted));
        Assert.Equal(WorkflowDispatchLifecycle.ScopeCancellationRequestedState,
            persisted.Metadata[WorkflowDispatchLifecycle.CancellationStateMetadataKey]);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, outbox.Status);
        Assert.True(outbox.RetryPolicy.RetryUntilAcknowledged);
        Assert.Equal(ClosedAt, outbox.RecordedAt);
        Assert.Equal(ClosedAt, outbox.AvailableAt);
        Assert.Same(intent, outbox.Intent);

        var replay = await stores.Scopes.CleanupAsync(
            scope,
            ClosedAt,
            10,
            new Dictionary<string, RuntimePostCommitIntent> { [pending.DispatchId] = intent });
        Assert.Equal(0, replay.CancellationQueued);
        Assert.Same(outbox, await stores.Outbox.FindAsync(outboxId));
    }

    [Fact]
    public async Task Cleanup_isolates_waited_production_and_other_scope_dispatches()
    {
        var stores = NewStores();
        var scope = NewScope("scope-target");
        var otherScope = NewScope("scope-other");
        var target = NewDispatch("target", scope);
        var waited = NewDispatch("waited", scope, WorkflowDispatchMode.WaitForCompletion);
        var production = NewDispatch("production", null, runKind: WorkflowRunKind.PublishedRun);
        var crossScope = NewDispatch("cross-scope", otherScope);
        await stores.Scopes.CreateAsync(scope, CreatedAt);
        await stores.Scopes.CreateAsync(otherScope, CreatedAt);
        foreach (var dispatch in new[] { target, waited, production, crossScope })
            await stores.Dispatches.SaveAsync(dispatch);
        await CloseAsync(stores.Scopes, scope);

        var result = await stores.Scopes.CleanupAsync(scope, ClosedAt, 10, EmptyIntents);

        Assert.Equal(1, result.Inspected);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, (await stores.Dispatches.FindAsync(target.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await stores.Dispatches.FindAsync(waited.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await stores.Dispatches.FindAsync(production.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await stores.Dispatches.FindAsync(crossScope.DispatchId))!.Status);
    }

    [Fact]
    public async Task One_hundred_duplicate_cleanup_attempts_converge_on_one_pending_cancellation()
    {
        var stores = NewStores();
        var scope = NewScope("scope-duplicate-cleanup");
        var target = NewDispatch("duplicate-cleanup", scope);
        await stores.Scopes.CreateAsync(scope, CreatedAt);
        await stores.Dispatches.SaveAsync(target);
        await CloseAsync(stores.Scopes, scope);

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            stores.Scopes.CleanupAsync(scope, ClosedAt, 10, EmptyIntents).AsTask()));

        Assert.Equal(1, results.Sum(result => result.CancelledBeforeAdmission));
        Assert.Equal(WorkflowDispatchStatus.Cancelled, (await stores.Dispatches.FindAsync(target.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchLifecycle.ScopeCancelledBeforeAdmissionState,
            (await stores.Dispatches.FindAsync(target.DispatchId))!.Metadata[WorkflowDispatchLifecycle.CancellationStateMetadataKey]);
    }

    [Fact]
    public async Task Marked_started_prefix_does_not_starve_a_later_pending_dispatch()
    {
        var stores = NewStores();
        var scope = NewScope("scope-marked-prefix");
        await stores.Scopes.CreateAsync(scope, CreatedAt);
        for (var i = 0; i < WorkflowDispatchQuery.MaximumTake + 1; i++)
        {
            var pending = NewDispatch($"a-marked-{i:D3}", scope);
            await stores.Dispatches.SaveAsync(pending);
            var admitted = await stores.Dispatches.TryAdmitAsync(pending.DispatchId, CreatedAt.AddMinutes(1));
            await stores.Dispatches.SaveAsync(
                WorkflowDispatchLifecycle.MarkTestScopeCancellationRequested(admitted.Record, ClosedAt));
        }

        var live = NewDispatch("z-live", scope);
        await stores.Dispatches.SaveAsync(live);
        await CloseAsync(stores.Scopes, scope);

        var result = await stores.Scopes.CleanupAsync(scope, ClosedAt, 1, EmptyIntents);

        Assert.Equal(1, result.Inspected);
        Assert.Equal(1, result.CancelledBeforeAdmission);
        Assert.Equal(WorkflowDispatchQuery.MaximumTake + 1, result.RemainingLive);
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(
            Assert.IsType<WorkflowDispatchRecord>(await stores.Dispatches.FindAsync(live.DispatchId))));
    }

    private static readonly IReadOnlyDictionary<string, RuntimePostCommitIntent> EmptyIntents =
        new Dictionary<string, RuntimePostCommitIntent>();

    private static async ValueTask CloseAsync(InMemoryWorkflowTestScopeStore store, WorkflowTestScope scope)
    {
        var result = await store.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown,
            ClosedAt));
        Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted, result.Disposition);
    }

    private static StoreFixture NewStores()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        return new StoreFixture(
            new InMemoryWorkflowTestScopeStore(state),
            new InMemoryWorkflowDispatchStore(state),
            new InMemoryRuntimeCheckpointCommitStore(state: state));
    }

    private static WorkflowTestScope NewScope(
        string scopeId = "scope-1",
        DateTimeOffset? expiresAt = null,
        string? tenantId = "tenant-1",
        string partition = "partition-1") =>
        new(scopeId, expiresAt ?? ExpiresAt, tenantId, new WorkflowExecutionPartition(partition));

    private static WorkflowDispatchRecord NewDispatch(
        string suffix,
        WorkflowTestScope? scope,
        WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget,
        WorkflowRunKind runKind = WorkflowRunKind.TestRun)
    {
        var parentId = $"parent-{suffix}";
        var activityId = $"activity-{suffix}";
        var identity = new WorkflowDispatchIdentity(parentId, activityId);
        var tenantId = scope?.TenantId ?? "tenant-1";
        var partition = scope?.Partition ?? new WorkflowExecutionPartition("partition-1");
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentId,
            activityId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity(
                $"artifact-{suffix}",
                $"definition-{suffix}",
                $"version-{suffix}",
                "1.0.0",
                $"sha256:{suffix}"),
            new WorkflowExecutableSourceProvenance(
                $"source-{suffix}",
                "WorkflowDefinitionVersion",
                $"version-{suffix}",
                "1.0.0",
                $"definition-{suffix}",
                $"version-{suffix}",
                "1.0.0",
                $"publication-{suffix}",
                $"slot-{suffix}"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            tenantId,
            partition,
            runKind,
            WorkflowExecutionAuthoritySnapshot.CreateRoot("test"),
            [],
            CreatedAt,
            CreatedAt,
            testScope: scope);
    }

    private static WorkflowDispatchIdentity Identity(WorkflowDispatchRecord dispatch) =>
        new(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);

    private static RuntimePostCommitIntent CancellationIntent(
        WorkflowDispatchRecord dispatch,
        DateTimeOffset recordedAt)
    {
        var identity = Identity(dispatch);
        return new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            dispatch.ParentWorkflowExecutionId,
            "Elsa.Activities.DispatchWorkflow.CancelChild",
            recordedAt,
            dispatch.ParentActivityExecutionId,
            identity.ChildCancelIdempotencyKey,
            payload: null,
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
            });
    }

    private sealed record StoreFixture(
        InMemoryWorkflowTestScopeStore Scopes,
        InMemoryWorkflowDispatchStore Dispatches,
        InMemoryRuntimeCheckpointCommitStore Outbox);
}
