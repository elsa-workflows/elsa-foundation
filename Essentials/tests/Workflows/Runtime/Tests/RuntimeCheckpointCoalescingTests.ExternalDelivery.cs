using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeCheckpointCoalescingTests
{
    private const string ExternalKind = "probe.external-delivery";
    private const string ExternalWorkflow = "wfexec-external-delivery";
    private static DateTimeOffset ExternalDeliveryAt => Now.AddSeconds(1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_retained_segment_delivers_external_work_through_its_durable_claim_and_reads_the_fresh_result(bool allKinds)
    {
        var (store, inner, _, commit) = await ExternalDeliveryFixtureAsync(persisted: true);
        var id = Assert.Single(commit.StateChanges.PostCommitOutbox).StateId;
        var kind = allKinds ? null : ExternalKind;
        var claim = Assert.Single(await store.ClaimAsync(new("external-owner", ExternalDeliveryAt, TimeSpan.FromSeconds(30), 10, ExternalWorkflow, kind)));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await inner.FindAsync(id))!.Status);
        var renewed = await store.RenewClaimAsync(claim, Now.AddSeconds(5), TimeSpan.FromSeconds(30));
        Assert.NotNull(renewed);
        Assert.Equal(Now.AddSeconds(35), (await inner.FindAsync(id))!.DeliveryVisibleAfter);
        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, await store.CompleteClaimAsync(
            new(renewed, new(id, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(6)))));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await inner.FindAsync(id))!.Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await store.FindAsync(id))!.Status);
        Assert.Empty(await store.GetDeliverableAsync(new(Now.AddMinutes(1), 10, ExternalWorkflow, kind)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_external_intent_is_not_deliverable_from_an_uncommitted_segment(bool allKinds)
    {
        var (store, inner, _, commit) = await ExternalDeliveryFixtureAsync(persisted: false);
        var kind = allKinds ? null : ExternalKind;
        Assert.Empty(await store.GetDeliverableAsync(new(ExternalDeliveryAt, 10, ExternalWorkflow, kind)));
        Assert.Empty(await store.ClaimAsync(new("external-owner", ExternalDeliveryAt, TimeSpan.FromSeconds(30), 10, ExternalWorkflow, kind)));
        Assert.Null(await inner.FindAsync(Assert.Single(commit.StateChanges.PostCommitOutbox).StateId));
    }

    [Fact]
    public async Task An_all_kinds_delivery_can_advance_owned_scheduler_work_without_exposing_an_uncommitted_external_effect()
    {
        var (_, inner, session, external) = await ExternalDeliveryFixtureAsync(persisted: false);
        var schedule = NewExternalDeliveryCommit("schedule", RuntimePostCommitIntentKinds.EnqueueSchedulerWork);
        session.BufferDeferred(schedule);
        var store = new CoalescingRuntimePostCommitOutboxStore(new CoalescingInner<IRuntimePostCommitOutboxStore>(inner),
            new FixedCoalescingSessionAccessor(session), new InMemoryWorkflowExecutionStateStore());
        Assert.Equal("schedule", Assert.Single(await store.GetDeliverableAsync(new(ExternalDeliveryAt, 10, ExternalWorkflow))).Intent.IntentId);
        var claim = Assert.Single(await store.ClaimAsync(new("owner", ExternalDeliveryAt, TimeSpan.FromSeconds(30), 10, ExternalWorkflow)));
        Assert.Equal("schedule", claim.Item.Intent.IntentId);
        await store.CompleteClaimAsync(new(claim, new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(1))));
        Assert.Empty(await store.GetDeliverableAsync(new(Now.AddMinutes(1), 10, ExternalWorkflow)));
        Assert.Null(await inner.FindAsync(Assert.Single(external.StateChanges.PostCommitOutbox).StateId));
    }

    [Fact]
    public async Task A_bounded_mixed_claim_advances_its_scheduler_continuation_then_the_durable_external_item()
    {
        var (store, inner, session, external) = await ExternalDeliveryFixtureAsync(persisted: true);
        var schedule = NewExternalDeliveryCommit("schedule", RuntimePostCommitIntentKinds.EnqueueSchedulerWork);
        session.BufferDeferred(schedule);
        var first = Assert.Single(await store.ClaimAsync(new("owner", ExternalDeliveryAt, TimeSpan.FromSeconds(30), 1, ExternalWorkflow)));
        Assert.Equal("schedule", first.Item.Intent.IntentId);
        await store.CompleteClaimAsync(new(first, new(first.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, ExternalDeliveryAt)));
        var second = Assert.Single(await store.ClaimAsync(new("owner", ExternalDeliveryAt, TimeSpan.FromSeconds(30), 1, ExternalWorkflow)));
        var id = Assert.Single(external.StateChanges.PostCommitOutbox).StateId;
        Assert.Equal(id, second.OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await inner.FindAsync(id))!.Status);
        await store.CompleteClaimAsync(new(second, new(id, RuntimePostCommitOutboxStatus.Delivered, ExternalDeliveryAt)));
        Assert.Empty(await store.ClaimAsync(new("owner", ExternalDeliveryAt, TimeSpan.FromSeconds(30), 1, ExternalWorkflow)));
    }

    [Fact]
    public async Task A_retained_segment_cannot_renew_or_complete_an_external_claim_after_another_owner_takes_it()
    {
        var (store, inner, _, external) = await ExternalDeliveryFixtureAsync(persisted: true);
        var id = Assert.Single(external.StateChanges.PostCommitOutbox).StateId;
        var original = Assert.Single(await store.ClaimAsync(new("owner", ExternalDeliveryAt, TimeSpan.FromSeconds(30), 1, ExternalWorkflow, ExternalKind)));
        var peer = Assert.Single(await inner.ClaimAsync(new("peer", Now.AddSeconds(32), TimeSpan.FromSeconds(30), 1, ExternalWorkflow, ExternalKind)));
        Assert.True(peer.FencingToken > original.FencingToken);
        Assert.Null(await store.RenewClaimAsync(original, Now.AddSeconds(33), TimeSpan.FromSeconds(30)));
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => store.CompleteClaimAsync(
            new(original, new(id, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(33)))).AsTask());
        var current = await store.FindAsync(id);
        Assert.Equal(peer.FencingToken, current!.DeliveryFencingToken);
        Assert.Equal("peer", current.DeliveringOwnerId);
    }

    private static async Task<(CoalescingRuntimePostCommitOutboxStore Store, InMemoryRuntimeCheckpointCommitStore Inner,
        RuntimeCoalescingSession Session, RuntimeCheckpointCommit Commit)> ExternalDeliveryFixtureAsync(bool persisted)
    {
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(ExternalWorkflow, new InMemoryWorkflowSchedulerWorkQueue(), new(), inner);
        var commit = NewExternalDeliveryCommit("external", ExternalKind);
        if (persisted)
        {
            await inner.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
            session.RecordCapFlushState(commit.StateChanges);
            session.MarkOutboxDurablyPersisted(commit.StateChanges.PostCommitOutbox);
            session.ClearBuffer();
        }
        else session.BufferDeferred(commit);
        var store = new CoalescingRuntimePostCommitOutboxStore(new CoalescingInner<IRuntimePostCommitOutboxStore>(inner),
            new FixedCoalescingSessionAccessor(session), new InMemoryWorkflowExecutionStateStore());
        return (store, inner, session, commit);
    }

    private static RuntimeCheckpointCommit NewExternalDeliveryCommit(string id, string kind)
    {
        var commit = NewEmptyCommit(ExternalWorkflow, id == "external" ? 1 : 2, "ExternalDelivery") with
        {
            PostCommitIntents = [new(id, ExternalWorkflow, kind, Now, null, null, null)]
        };
        return commit with { StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit)), PostCommitIntents = [] };
    }
}
