using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Proves that routing the post-commit outbox's pending-intent comparison through the versioned
/// serializer (W3) preserves <c>SavePendingAsync</c> idempotency. The store's
/// <c>IsSamePendingIntent</c> check now compares intents via
/// <c>IGroundworkRuntimeDocumentSerializer.SerializeForComparison</c>; version-stamping must not make an
/// identical redelivered intent look different (which would duplicate work for already-suspended
/// workflows) nor make a genuinely different intent look the same (which would silently drop work).
/// </summary>
public sealed class GroundworkRuntimePostCommitOutboxContinuityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Resaving_The_Same_Pending_Intent_Is_Idempotent(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1"));
        // A redelivery of the identical intent for an already-suspended workflow. The serialized-intent
        // comparison must treat it as the same intent and leave a single pending item.
        await store.SavePendingAsync(Pending("item-1", "wf-1"));

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
        Assert.Equal(new[] { "item-1" }, deliverable.Select(x => x.OutboxItemId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Resaving_A_Different_Intent_Under_The_Same_Id_Is_Rejected(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = new GroundworkRuntimePostCommitOutboxStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SavePendingAsync(Pending("item-1", "wf-1", kind: "a"));

        // A different intent (different kind) under the same id serializes differently, so it must NOT be
        // treated as the same pending intent: the store rejects it rather than silently ignoring the change.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.SavePendingAsync(Pending("item-1", "wf-1", kind: "b")));

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
        Assert.Equal("a", Assert.Single(deliverable).Intent.Kind);
    }

    private static RuntimePostCommitOutboxItem Pending(string outboxItemId, string workflowExecutionId, string kind = "publish") => new(
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
        retryPolicy: null);
}
