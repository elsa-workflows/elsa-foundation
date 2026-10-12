using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.RuntimeCheckpointCommitContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Test-scope cleanup must converge when it finds a detached child's cancellation already recorded (spec 102 FR-014): a
/// concurrent or repeated cleaner may arrive after delivery has claimed, delivered, or retried that item. Every store must
/// accept the recorded responsibility and leave the item exactly as it is, and must still fail loudly when the recorded item
/// carries a different responsibility under the same identity.
/// </summary>
public sealed class WorkflowTestScopeCleanupContractTests
{
    private const string CancelChildIntentKind = "Elsa.Activities.DispatchWorkflow.CancelChild";

    public static TheoryData<string> StoreData => RuntimeCheckpointCommitContract.StoreData();

    public static TheoryData<string, RuntimePostCommitOutboxStatus> ProgressedCancellationData()
    {
        var data = new TheoryData<string, RuntimePostCommitOutboxStatus>();
        foreach (var store in RuntimeCheckpointCommitContract.Stores)
        foreach (var status in new[] { RuntimePostCommitOutboxStatus.Delivering, RuntimePostCommitOutboxStatus.Delivered, RuntimePostCommitOutboxStatus.FailedRetryable })
            data.Add(store, status);
        return data;
    }

    [Theory]
    [MemberData(nameof(ProgressedCancellationData))]
    public async Task A_late_cleanup_converges_on_a_cancellation_whose_delivery_already_progressed(string store, RuntimePostCommitOutboxStatus progressed)
    {
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);
        var arranged = await ArrangeClosingScopeWithAdmittedChildAsync(backend, "scope-late-cleanup");

        // The cancellation an earlier cleaner recorded, which delivery has since progressed.
        await SeedCancellationAsync(backend, arranged, arranged.Intent);
        await ProgressAsync(backend, arranged.ItemId, progressed);
        var before = (await backend.Outbox.FindAsync(arranged.ItemId))!;
        Assert.Equal(progressed, before.Status);

        var result = await CleanupAsync(backend, arranged, arranged.Intent);

        Assert.Equal(1, result.CancellationQueued);
        var after = (await backend.Outbox.FindAsync(arranged.ItemId))!;
        Assert.Equal(progressed, after.Status);
        Assert.Equal(before.DeliveryAttemptCount, after.DeliveryAttemptCount);
        Assert.Equal(before.DeliveredAt, after.DeliveredAt);
        Assert.Equal(before.DeliveringOwnerId, after.DeliveringOwnerId);
        Assert.True(before.IsEquivalentTo(after), "The recorded cancellation must be left exactly as delivery left it.");
        Assert.All(
            await backend.Delivery.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(OccurredAt.AddDays(1), 100, intentKind: CancelChildIntentKind)),
            item => Assert.Equal(arranged.ItemId, item.OutboxItemId));
        var child = (await backend.Dispatches.FindAsync(arranged.Started.DispatchId))!;
        Assert.Equal(WorkflowDispatchStatus.Started, child.Status);
        Assert.True(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(child));
        Assert.Equal(WorkflowTestScopeState.Closing, (await backend.Scopes.FindAsync(arranged.Scope.ScopeId))!.State);
    }

    [Theory]
    [MemberData(nameof(StoreData))]
    public async Task A_cleanup_fails_loudly_when_the_recorded_cancellation_carries_a_different_responsibility(string store)
    {
        await using var backend = await RuntimeCheckpointCommitContractBackend.CreateAsync(store);
        var arranged = await ArrangeClosingScopeWithAdmittedChildAsync(backend, "scope-conflicting-cleanup");
        // Same item identity, but a responsibility recorded at a different moment: not this cleanup's responsibility.
        await SeedCancellationAsync(backend, arranged, CancellationIntent(arranged.Started, arranged.ResponsibilityAt.AddSeconds(-30)));
        var before = (await backend.Outbox.FindAsync(arranged.ItemId))!;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CleanupAsync(backend, arranged, arranged.Intent).AsTask());

        Assert.Equal("The workflow test-scope cancellation outbox item conflicts with committed responsibility.", exception.Message);
        Assert.True(before.IsEquivalentTo(await backend.Outbox.FindAsync(arranged.ItemId)), "A conflicting cleanup must not overwrite the recorded item.");
    }

    private static async Task<ArrangedCleanup> ArrangeClosingScopeWithAdmittedChildAsync(RuntimeCheckpointCommitContractBackend backend, string scopeId)
    {
        var scope = TestScope(scopeId);
        await backend.Scopes.CreateAsync(scope, OccurredAt.AddMinutes(-10));
        var pending = PendingDispatch("workflow-parent", "activity-detached", testScope: scope);
        await backend.Dispatches.SaveAsync(pending);
        var started = (await backend.Admissions.TryAdmitAsync(pending.DispatchId, OccurredAt.AddMinutes(-5))).Record;
        await backend.Scopes.CloseAsync(new WorkflowTestScopeCloseRequest(scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, OccurredAt.AddMinutes(-1)));
        var responsibilityAt = (await backend.Scopes.FindAsync(scope.ScopeId))!.ClosingAt!.Value;
        var itemId = new WorkflowDispatchIdentity(started.ParentWorkflowExecutionId, started.ParentActivityExecutionId)
            .ChildCancelOutboxItemId($"test-scope:{scope.ScopeId}");
        return new ArrangedCleanup(scope, started, responsibilityAt, CancellationIntent(started, responsibilityAt), itemId);
    }

    private static Task SeedCancellationAsync(RuntimeCheckpointCommitContractBackend backend, ArrangedCleanup arranged, RuntimePostCommitIntent intent) =>
        backend.SeedPendingOutboxItemAsync(new RuntimePostCommitOutboxItem(
            arranged.ItemId, intent, RuntimePostCommitOutboxStatus.Pending, arranged.ResponsibilityAt, arranged.ResponsibilityAt,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1))));

    private static ValueTask<WorkflowTestScopeCleanupResult> CleanupAsync(RuntimeCheckpointCommitContractBackend backend, ArrangedCleanup arranged, RuntimePostCommitIntent intent) =>
        backend.Cleanup.CleanupAsync(
            arranged.Scope, arranged.ResponsibilityAt, 10, new Dictionary<string, RuntimePostCommitIntent> { [arranged.Started.DispatchId] = intent });

    private static async Task ProgressAsync(RuntimeCheckpointCommitContractBackend backend, string itemId, RuntimePostCommitOutboxStatus progressed)
    {
        var claim = Assert.Single(await backend.Claims.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("deliverer", OccurredAt, TimeSpan.FromMinutes(5), 10, intentKind: CancelChildIntentKind)));
        Assert.Equal(itemId, claim.OutboxItemId);
        if (progressed == RuntimePostCommitOutboxStatus.Delivering)
            return;
        await backend.Claims.RecordDeliveryResultAsync(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(itemId, progressed, OccurredAt.AddSeconds(1), progressed == RuntimePostCommitOutboxStatus.FailedRetryable ? "transient" : null));
    }

    private static RuntimePostCommitIntent CancellationIntent(WorkflowDispatchRecord started, DateTimeOffset requestedAt)
    {
        var identity = new WorkflowDispatchIdentity(started.ParentWorkflowExecutionId, started.ParentActivityExecutionId);
        return new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            started.ParentWorkflowExecutionId,
            CancelChildIntentKind,
            requestedAt,
            started.ParentActivityExecutionId,
            identity.ChildCancelIdempotencyKey,
            payload: null,
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = started.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = started.ChildWorkflowExecutionId
            });
    }

    private sealed record ArrangedCleanup(
        WorkflowTestScope Scope,
        WorkflowDispatchRecord Started,
        DateTimeOffset ResponsibilityAt,
        RuntimePostCommitIntent Intent,
        string ItemId);
}
