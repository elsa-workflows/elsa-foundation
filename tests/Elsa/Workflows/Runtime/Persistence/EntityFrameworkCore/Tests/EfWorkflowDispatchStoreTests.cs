using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    public async Task Equal_time_dispatch_pages_follow_the_full_logical_id_ordinal_order()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var store = new EfWorkflowDispatchStore(context, new FixedAccessor("tenant-a"));
        var records = Enumerable.Range(0, 20)
            .Select(index => Pending("parent-order", $"activity-{index:D2}", createdAt: Now))
            .ToArray();
        foreach (var record in records.Reverse())
            await store.SaveAsync(record);

        var expected = records.Select(record => record.DispatchId)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var actual = new List<string>();
        DateTimeOffset? afterCreatedAt = null;
        string? afterDispatchId = null;
        do
        {
            var page = await store.QueryAsync(new WorkflowDispatchQuery(
                parentWorkflowExecutionId: "parent-order",
                take: 3,
                afterCreatedAt: afterCreatedAt,
                afterDispatchId: afterDispatchId));
            if (page.Count == 0)
                break;
            actual.AddRange(page.Select(record => record.DispatchId));
            var last = page.Last();
            afterCreatedAt = last.CreatedAt;
            afterDispatchId = last.DispatchId;
        } while (actual.Count < records.Length);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Sqlite_cancellation_and_snapshot_delete_are_fenced()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var store = new EfWorkflowDispatchStore(context, new FixedAccessor("tenant-a"));
        // Parent cancellation propagates only to a waited child.
        var pending = Pending("parent-a", "activity-a", mode: WorkflowDispatchMode.WaitForCompletion);
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

    /// <summary>
    /// A completion whose transaction committed but whose acknowledgement was lost reconciles against what it wrote. The
    /// child already exists, so the start was written as delivered and the parent resume was discarded: a missing
    /// follow-up is exactly what the completion persisted, not a sign that it failed.
    /// </summary>
    [Fact]
    public async Task Sqlite_lost_completion_ack_reconciles_a_start_delivered_on_child_evidence()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new LoseCommitAcknowledgementInterceptor();
        await using var context = database.Open(interceptors: interceptor);
        var access = new FixedAccessor("tenant-a");
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var dispatch = Pending("parent-lost-ack", "activity-lost-ack", mode: WorkflowDispatchMode.WaitForCompletion);
        await dispatchStore.SaveAsync(dispatch);
        await new EfWorkflowExecutionStateStore(context, access, Codec).SaveAsync(ChildOf(dispatch));
        var item = StartItem("start-lost-ack", dispatch);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        await outbox.SavePendingAsync(item);
        var claim = Assert.Single(await outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest("worker", Now, TimeSpan.FromMinutes(1), 1)));
        var failedAt = Now.AddSeconds(1);
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(dispatch, item.OutboxItemId, 0, 1, Now, failedAt);
        var followUp = ParentResume(failed, failedAt);

        interceptor.LoseNextAcknowledgement();
        var outcome = await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, failedAt, "delivery-failed"),
            failed,
            followUp));

        Assert.True(interceptor.LostAcknowledgement);
        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.DeliveredOnChildEvidence, outcome);
        await using var restarted = database.Open();
        var persistedOutbox = new EfRuntimePostCommitOutboxStore(restarted, access);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await persistedOutbox.FindAsync(item.OutboxItemId))!.Status);
        Assert.Null(await persistedOutbox.FindAsync(followUp.OutboxItemId));
        Assert.Equal(WorkflowDispatchStatus.Started, (await new EfWorkflowDispatchStore(restarted, access).FindAsync(dispatch.DispatchId))!.Status);
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
        var sibling = Pending("parent-invalid-sibling", "activity-invalid-sibling");
        await dispatchStore.SaveAsync(sibling);
        await StageSiblingRevisionAsync(context, sibling.DispatchId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now.AddSeconds(1), "delivery-failed"),
            invalid)).AsTask());
        await context.SaveChangesAsync();
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await outbox.FindAsync(item.OutboxItemId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(sibling.DispatchId))!.Status);
        Assert.Equal(2, await ReadRevisionAsync(context, sibling.DispatchId));
    }

    [Fact]
    public async Task Sqlite_failed_transaction_begin_preserves_tracked_rows_and_sibling_changes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailTransactionStartInterceptor();
        await using var context = database.Open(interceptors: interceptor);
        var access = new FixedAccessor("tenant-a");
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        var dispatch = Pending("parent-begin-failure", "activity-begin-failure");
        await dispatchStore.SaveAsync(dispatch);
        var item = StartItem("start-begin-failure", dispatch);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access);
        await outbox.SavePendingAsync(item);
        var claim = Assert.Single(await outbox.ClaimAsync(new RuntimePostCommitOutboxClaimRequest("worker", Now, TimeSpan.FromMinutes(1), 1)));
        var failedAt = Now.AddSeconds(1);
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(dispatch, item.OutboxItemId, 0, 1, Now, failedAt);
        var sibling = Pending("parent-begin-failure-sibling", "activity-begin-failure-sibling");
        await dispatchStore.SaveAsync(sibling);
        await StageSiblingRevisionAsync(context, sibling.DispatchId);

        interceptor.FailNextBegin();
        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, failedAt, "delivery-failed"),
            failed)).AsTask());

        await context.SaveChangesAsync();
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await outbox.FindAsync(item.OutboxItemId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(sibling.DispatchId))!.Status);
        Assert.Equal(2, await ReadRevisionAsync(context, sibling.DispatchId));
    }

    [Fact]
    public async Task Sqlite_failed_dispatch_deletes_detach_rows_and_preserve_sibling_changes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailWorkflowDispatchDeleteInterceptor();
        await using var context = database.Open(interceptors: interceptor);
        var access = new FixedAccessor("tenant-a");
        var store = new EfWorkflowDispatchStore(context, access);
        // Parent cancellation propagates only to a waited child.
        var first = Pending("parent-delete-first", "activity-delete-first", mode: WorkflowDispatchMode.WaitForCompletion);
        var second = Pending("parent-delete-second", "activity-delete-second", mode: WorkflowDispatchMode.WaitForCompletion);
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        var firstCancelled = await store.ApplyCancellationAsync(new WorkflowDispatchCancellationRequest(
            first.DispatchId,
            first.ParentWorkflowExecutionId,
            first.ParentActivityExecutionId,
            first.ChildWorkflowExecutionId,
            Now.AddMinutes(1)));
        var secondCancelled = await store.ApplyCancellationAsync(new WorkflowDispatchCancellationRequest(
            second.DispatchId,
            second.ParentWorkflowExecutionId,
            second.ParentActivityExecutionId,
            second.ChildWorkflowExecutionId,
            Now.AddMinutes(1)));

        var firstSibling = Pending("parent-delete-first-sibling", "activity-delete-first-sibling");
        await store.SaveAsync(firstSibling);
        await StageSiblingRevisionAsync(context, firstSibling.DispatchId);
        interceptor.FailNextDelete();
        var firstFailure = await Assert.ThrowsAsync<DbUpdateException>(() => store.TryDeleteAsync(firstCancelled.Record).AsTask());
        Assert.IsType<InvalidOperationException>(firstFailure.InnerException);
        await context.SaveChangesAsync();
        Assert.Equal(2, await ReadRevisionAsync(context, firstSibling.DispatchId));

        var secondSibling = Pending("parent-delete-second-sibling", "activity-delete-second-sibling");
        await store.SaveAsync(secondSibling);
        await StageSiblingRevisionAsync(context, secondSibling.DispatchId);
        interceptor.FailNextDelete();
        var secondFailure = await Assert.ThrowsAsync<DbUpdateException>(() => store.DeleteAsync(secondCancelled.Record.DispatchId).AsTask());
        Assert.IsType<InvalidOperationException>(secondFailure.InnerException);
        await context.SaveChangesAsync();
        Assert.Equal(2, await ReadRevisionAsync(context, secondSibling.DispatchId));

        Assert.NotNull(await store.FindAsync(firstCancelled.Record.DispatchId));
        Assert.NotNull(await store.FindAsync(secondCancelled.Record.DispatchId));
        Assert.NotNull(await store.FindAsync(firstSibling.DispatchId));
        Assert.NotNull(await store.FindAsync(secondSibling.DispatchId));
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
    public void Registration_replaces_all_public_dispatch_contracts_and_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();

        Assert.Contains(services, x => x.ServiceType == typeof(EfWorkflowDispatchStore));
        Assert.Equal(RuntimeWorkflowDispatchStoreBackend.EntityFramework, RuntimeWorkflowDispatchStoreBackend.Find(services)!.Name);
        Assert.All(new[]
        {
            typeof(IWorkflowDispatchStore),
            typeof(IWorkflowDispatchQueryStore),
            typeof(IWorkflowDispatchDeleteStore),
            typeof(IWorkflowDispatchRetentionRootStore),
            typeof(IWorkflowDispatchAdmissionStore),
            typeof(IWorkflowDispatchCancellationStore)
        }, serviceType =>
        {
            var descriptors = services.Where(x => x.ServiceType == serviceType).ToArray();
            Assert.Single(descriptors);
            Assert.NotNull(descriptors[0].ImplementationFactory);
            Assert.True(RuntimeWorkflowDispatchStoreBackend.Find(services)!.Owns(descriptors[0]));
        });
    }

    [Fact]
    public void Registration_rejects_an_explicit_dispatch_contract_without_mutation()
    {
        var services = new ServiceCollection();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddSingleton<IWorkflowDispatchStore, InMemoryWorkflowDispatchStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeWorkflowDispatchEntityFrameworkCore());
        Assert.Equal(before, services);
        Assert.Null(RuntimeWorkflowDispatchStoreBackend.Find(services));
    }

    [Fact]
    public void Registration_rejects_a_foreign_factory_even_if_its_name_resembles_runtime_core()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        services.Remove(services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchQueryStore)));
        services.AddScoped<IWorkflowDispatchQueryStore>(ForeignRuntimeCoreServiceCollectionExtensions.ResolveDispatchQuery);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeWorkflowDispatchEntityFrameworkCore());
        Assert.Equal(before, services);
    }

    private static class ForeignRuntimeCoreServiceCollectionExtensions
    {
        public static IWorkflowDispatchQueryStore ResolveDispatchQuery(IServiceProvider _) =>
            throw new InvalidOperationException("The foreign registration must not be resolved.");
    }

    private static async Task StageSiblingRevisionAsync(BookmarkStateSqliteDbContext context, string dispatchId)
    {
        var row = await context.WorkflowDispatches.SingleAsync(x =>
            x.DispatchId == EfRelationalIdentity.Encode(dispatchId));
        row.Revision++;
    }

    private static Task<long> ReadRevisionAsync(BookmarkStateSqliteDbContext context, string dispatchId) =>
        context.WorkflowDispatches.AsNoTracking()
            .Where(x => x.DispatchId == EfRelationalIdentity.Encode(dispatchId))
            .Select(x => x.Revision)
            .SingleAsync();

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

    private static readonly HmacRuntimeRecoveryContinuationCodec Codec = new(
        Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-workflow-dispatch-store-tests-signing-key-32" }));

    /// <summary>The child execution <paramref name="dispatch"/> started, in exactly the context it retains.</summary>
    private static WorkflowExecutionState ChildOf(WorkflowDispatchRecord dispatch) =>
        new(
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
            Authority = dispatch.Authority,
            DispatchNestingDepth = dispatch.DispatchNestingDepth
        };

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

        public BookmarkStateSqliteDbContext Open(string scope = "tenant-a", params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            return new BookmarkStateSqliteDbContext(builder.Options);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    /// <summary>Lets the next transaction commit, then fails as if the commit's acknowledgement had been lost.</summary>
    private sealed class LoseCommitAcknowledgementInterceptor : DbTransactionInterceptor
    {
        private int loseNext;

        public bool LostAcknowledgement { get; private set; }

        public void LoseNextAcknowledgement() => Interlocked.Exchange(ref loseNext, 1);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref loseNext, 0) == 0)
                return Task.CompletedTask;

            LostAcknowledgement = true;
            throw new InvalidOperationException("Simulated lost commit acknowledgement.");
        }
    }

    private sealed class FailTransactionStartInterceptor : DbTransactionInterceptor
    {
        private int failNextBegin;

        public void FailNextBegin() => Interlocked.Exchange(ref failNextBegin, 1);

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref failNextBegin, 0) == 1)
                throw new InvalidOperationException("Simulated transaction-begin failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailWorkflowDispatchDeleteInterceptor : DbCommandInterceptor
    {
        private int failNextDelete;

        public void FailNextDelete() => Interlocked.Exchange(ref failNextDelete, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfArmed(DbCommand command)
        {
            if (command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(RuntimeWorkflowDispatchEfModule.TableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref failNextDelete, 0) == 1)
                throw new InvalidOperationException("Simulated workflow dispatch delete failure.");
        }
    }
}
