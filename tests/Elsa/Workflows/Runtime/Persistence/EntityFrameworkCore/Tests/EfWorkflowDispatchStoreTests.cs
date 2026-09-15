using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowDispatchStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_round_trips_replay_and_conflicting_transition()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var context = database.Open())
        {
            var store = new EfWorkflowDispatchStore(context, new FixedAccessor("tenant-a"));
            var pending = Pending("parent-a", "activity-a");
            Assert.Same(pending, await store.SaveAsync(pending));
            var replay = await store.SaveAsync(pending);
            Assert.Equal(pending.DispatchId, replay.DispatchId);
            var admitted = await store.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(1));
            Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admitted.Disposition);
            var already = await store.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(2));
            Assert.Equal(WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, already.Disposition);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(pending.TransitionTo(WorkflowDispatchStatus.Pending, Now.AddMinutes(3))).AsTask());
        }

        await using var restarted = database.Open();
        var persisted = await new EfWorkflowDispatchStore(restarted, new FixedAccessor("tenant-a"))
            .FindAsync(new WorkflowDispatchIdentity("parent-a", "activity-a").DispatchId);
        Assert.Equal(WorkflowDispatchStatus.Started, persisted!.Status);
    }

    [Fact]
    public async Task Sqlite_query_is_bounded_ordered_and_scope_isolated()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var context = database.Open())
        {
            var store = new EfWorkflowDispatchStore(context, new FixedAccessor("tenant-a"));
            await store.SaveAsync(Pending("parent-a", "activity-c", createdAt: Now.AddMinutes(2)));
            await store.SaveAsync(Pending("parent-a", "activity-a", createdAt: Now));
            await store.SaveAsync(Pending("parent-b", "activity-b", createdAt: Now.AddMinutes(1)));
            var page = await store.QueryAsync(new WorkflowDispatchQuery(parentWorkflowExecutionId: "parent-a", take: 1));
            Assert.Single(page);
            Assert.Equal("activity-a", page.Single().ParentActivityExecutionId);
            var next = await store.QueryAsync(new WorkflowDispatchQuery(parentWorkflowExecutionId: "parent-a", take: 10, afterCreatedAt: page.Single().CreatedAt, afterDispatchId: page.Single().DispatchId));
            Assert.Equal("activity-c", Assert.Single(next).ParentActivityExecutionId);
            Assert.Equal(["artifact-activity-a", "artifact-activity-b", "artifact-activity-c"], await store.ListPinnedExecutableArtifactIdsAsync());
        }

        await using var otherScope = database.Open("tenant-b");
        Assert.Empty(await new EfWorkflowDispatchStore(otherScope, new FixedAccessor("tenant-b"))
            .QueryAsync(new WorkflowDispatchQuery(parentWorkflowExecutionId: "parent-a")));
    }

    [Fact]
    public async Task Sqlite_cancellation_and_snapshot_delete_are_fenced()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var store = new EfWorkflowDispatchStore(context, new FixedAccessor("tenant-a"));
        var pending = Pending("parent-a", "activity-a");
        await store.SaveAsync(pending);
        var request = new WorkflowDispatchCancellationRequest(pending.DispatchId, pending.ParentWorkflowExecutionId, pending.ParentActivityExecutionId, pending.ChildWorkflowExecutionId, Now.AddMinutes(1));
        var cancelled = await store.ApplyCancellationAsync(request);
        Assert.Equal(WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission, cancelled.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, cancelled.Record.Status);
        Assert.False(await store.TryDeleteAsync(pending));
        Assert.True(await store.TryDeleteAsync(cancelled.Record));
        Assert.Null(await store.FindAsync(pending.DispatchId));
    }

    [Fact]
    public async Task Sqlite_tampered_projection_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var store = new EfWorkflowDispatchStore(context, new FixedAccessor("tenant-a"));
        var record = Pending("parent-a", "activity-a");
        await store.SaveAsync(record);
        var row = await context.WorkflowDispatches.SingleAsync();
        row.Status = (int)WorkflowDispatchStatus.Started;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.FindAsync(record.DispatchId).AsTask());
    }

    [Fact]
    public async Task Sqlite_atomic_outbox_completion_projects_dispatch_failure_and_wait_follow_up()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var dispatch = Pending("parent-failure", "activity-failure", mode: WorkflowDispatchMode.WaitForCompletion);
        await dispatchStore.SaveAsync(dispatch);
        var item = StartItem("start-failure", dispatch);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        await outbox.SavePendingAsync(item);
        var claim = Assert.Single(await outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest("worker", Now, TimeSpan.FromMinutes(1), 1)));
        var failedAt = Now.AddSeconds(1);
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(dispatch, item.OutboxItemId, 0, 1, Now, failedAt);
        var followUp = ParentResume(failed, failedAt);

        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, failedAt, "delivery-failed"),
            failed,
            followUp));

        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await outbox.FindAsync(item.OutboxItemId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await outbox.FindAsync(followUp.OutboxItemId))!.Status);
    }

    [Fact]
    public async Task Sqlite_invalid_dispatch_projection_rolls_back_claimed_outbox()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var dispatch = Pending("parent-invalid", "activity-invalid");
        await dispatchStore.SaveAsync(dispatch);
        var item = StartItem("start-invalid", dispatch);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        await outbox.SavePendingAsync(item);
        var claim = Assert.Single(await outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest("worker", Now, TimeSpan.FromMinutes(1), 1)));
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(dispatch, item.OutboxItemId, 0, 1, Now, Now.AddSeconds(1));
        var invalid = new WorkflowDispatchRecord(
            failed.DispatchId,
            failed.ParentWorkflowExecutionId,
            failed.ParentActivityExecutionId,
            failed.ChildWorkflowExecutionId,
            failed.ChildExecutable,
            failed.ChildSource,
            failed.Mode,
            failed.Status,
            "wrong-correlation",
            failed.TenantId,
            failed.Partition,
            failed.RunKind,
            failed.Authority,
            failed.InputDescriptors,
            failed.CreatedAt,
            failed.UpdatedAt,
            failed.Metadata,
            failed.DispatchNestingDepth,
            failed.TestScope);

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now.AddSeconds(1), "delivery-failed"),
            invalid)).AsTask());
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await outbox.FindAsync(item.OutboxItemId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
    }

    [Fact]
    public async Task Sqlite_redrive_atomically_reopens_matching_dead_letter_and_is_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var access = new FixedAccessor("tenant-a");
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var dispatch = Pending("parent-redrive", "activity-redrive");
        await dispatchStore.SaveAsync(dispatch);
        var item = StartItem("start-redrive", dispatch, new RuntimePostCommitRetryPolicy(1, TimeSpan.FromSeconds(1)));
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        await outbox.SavePendingAsync(item);
        var claim = Assert.Single(await outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest("worker", Now, TimeSpan.FromMinutes(1), 1)));
        var failedAt = Now.AddSeconds(1);
        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, failedAt, "delivery-failed"),
            WorkflowDispatchLifecycle.TransitionToDispatchFailed(dispatch, item.OutboxItemId, 0, 1, Now, failedAt)));

        var request = new WorkflowDispatchRedriveRequest(dispatch.DispatchId, "redrive-1", Now.AddMinutes(1));
        var accepted = await outbox.RedriveAsync(request);
        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, accepted.Disposition);
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await outbox.FindAsync(item.OutboxItemId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchRedriveDisposition.AlreadyApplied, (await outbox.RedriveAsync(request)).Disposition);
        Assert.Equal(WorkflowDispatchRedriveDisposition.ActiveConflict, (await outbox.RedriveAsync(new WorkflowDispatchRedriveRequest(dispatch.DispatchId, "redrive-2", Now.AddMinutes(2)))).Disposition);
    }

    [Fact]
    public void Preview_registration_keeps_public_dispatch_contracts_unreplaced()
    {
        var services = new ServiceCollection();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        Assert.Contains(services, x => x.ServiceType == typeof(EfWorkflowDispatchStore));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IWorkflowDispatchStore));
    }

    private static WorkflowDispatchRecord Pending(string parent, string activity, DateTimeOffset? createdAt = null, WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget)
    {
        var identity = new WorkflowDispatchIdentity(parent, activity);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity($"artifact-{activity}", "definition-child", "version-child", "1", $"hash-{activity}"),
            new WorkflowExecutableSourceProvenance($"source-{activity}", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            "tenant-a",
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            createdAt ?? Now,
            createdAt ?? Now,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" });
    }

    private static RuntimePostCommitOutboxItem StartItem(string id, WorkflowDispatchRecord dispatch, RuntimePostCommitRetryPolicy? retryPolicy = null) => new(
        id,
        new RuntimePostCommitIntent(
            new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).StartIntentId,
            dispatch.ParentWorkflowExecutionId,
            WorkflowDispatchLifecycle.StartChildIntentKind,
            Now,
            dispatch.ParentActivityExecutionId,
            new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).StartIdempotencyKey,
            null,
            new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId }),
        RuntimePostCommitOutboxStatus.Pending,
        Now,
        Now,
        retryPolicy ?? new RuntimePostCommitRetryPolicy(1, TimeSpan.FromSeconds(1)));

    private static RuntimePostCommitOutboxItem ParentResume(WorkflowDispatchRecord dispatch, DateTimeOffset recordedAt)
    {
        var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
        return new RuntimePostCommitOutboxItem(
            identity.WaitFailureResumeOutboxItemId(WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch)),
            new RuntimePostCommitIntent(
                identity.ParentResumeIntentId,
                dispatch.ParentWorkflowExecutionId,
                WorkflowDispatchLifecycle.ResumeParentIntentKind,
                recordedAt,
                dispatch.ParentActivityExecutionId,
                identity.ParentResumeIdempotencyKey,
                null,
                new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                    [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
                }),
            RuntimePostCommitOutboxStatus.Pending,
            recordedAt,
            recordedAt,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class TestDatabase(SqliteConnection connection) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection);
        }

        public BookmarkStateSqliteDbContext Open(string scope = "tenant-a") =>
            new(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
