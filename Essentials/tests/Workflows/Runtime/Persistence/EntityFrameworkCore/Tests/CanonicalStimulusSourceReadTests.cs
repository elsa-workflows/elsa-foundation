using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Canonical source reader controls only. The historical cohort fixtures here are not modern routing/host acceptance.
public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Canonical_source_read_returns_absence_then_the_actual_full_or_empty_winner_to_stale_context(bool ef, bool empty)
    {
        await using var harness = await Harness.Create(ef); var stale = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        Assert.Null((await harness.Delivery.ReadSourceAsync(stale)).Claim.Item.RecipientSnapshot);
        var saved = await harness.Delivery.InstallAsync(stale, Snapshot(stale, empty));
        var canonical = await harness.Reopen().ReadSourceAsync(stale);
        Assert.True(saved.IsEquivalentTo(canonical.Claim.Item.RecipientSnapshot));
        Assert.Equal(stale.IntentFingerprint, canonical.IntentFingerprint); Assert.Equal(stale.SourceCommitId, canonical.SourceCommitId);
        Assert.Equal(stale.Scope, canonical.Scope); Assert.Null(stale.Claim.Item.RecipientSnapshot);
        Assert.Equal(0, canonical.Claim.Item.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Canonical_source_read_uses_actual_renewed_deadline_and_refuses_expiry(bool ef, bool renew)
    {
        await using var harness = await Harness.Create(ef); var stale = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        if (renew) Assert.NotNull(await harness.Claims.RenewClaimAsync(stale.Claim, Now.AddSeconds(30), TimeSpan.FromMinutes(3)));
        harness.Clock.Advance(TimeSpan.FromSeconds(31));
        if (renew) Assert.Equal(Now.AddSeconds(30).AddMinutes(3), (await harness.Reopen().ReadSourceAsync(stale)).Claim.VisibleAfter);
        else await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => harness.Delivery.ReadSourceAsync(stale).AsTask());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Canonical_source_read_refuses_settled_or_reclaimed_source_then_admits_new_owner(bool ef, bool reclaim)
    {
        await using var harness = await Harness.Create(ef); var stale = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var winner = await harness.Delivery.InstallAsync(stale, Snapshot(stale, true));
        await harness.Claims.RecordDeliveryResultAsync(stale.Claim, new(stale.SourceOutboxItemId,
            reclaim ? RuntimePostCommitOutboxStatus.FailedRetryable : RuntimePostCommitOutboxStatus.Delivered, Now, reclaim ? "retry" : null));
        RuntimePostCommitOutboxClaim? current = null;
        if (reclaim)
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            current = Assert.Single(await harness.Claims.ClaimAsync(new("peer", Now.AddSeconds(2), TimeSpan.FromMinutes(1), 1)));
        }
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => harness.Delivery.ReadSourceAsync(stale).AsTask());
        if (current is not null)
        {
            var source = await harness.Reopen().ReadSourceAsync(new(current, Scope));
            Assert.Equal("peer", source.Claim.OwnerId); Assert.True(winner.IsEquivalentTo(source.Claim.Item.RecipientSnapshot));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Canonical_source_read_refuses_unmarked_legacy_responsibility(bool ef)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(protocol: false), Scope);
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.ReadSourceAsync(source).AsTask());
        Assert.False(refusal.IsUnavailable); Assert.IsType<InvalidDataException>(refusal.InnerException);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
    }

    [Fact]
    public async Task Canonical_EF_source_read_refuses_dirty_or_local_transaction_without_flushing_caller_work()
    {
        await using var harness = await Harness.Create(true); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        var context = harness.Context!; var row = await context.WorkflowExecutionStates.SingleAsync(); var oldRevision = row.Revision;
        row.Revision++; context.ChangeTracker.DetectChanges();
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.ReadSourceAsync(source).AsTask());
        Assert.False(refusal.IsUnavailable); Assert.Equal(EntityState.Modified, context.Entry(row).State);
        Assert.Null((await harness.Lookup.FindAsync(source.SourceOutboxItemId))!.RecipientSnapshot);
        context.Entry(row).State = EntityState.Detached;
        Assert.Equal(oldRevision, (await context.WorkflowExecutionStates.AsNoTracking().SingleAsync()).Revision);
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.ReadSourceAsync(source).AsTask());
            Assert.Same(transaction, context.Database.CurrentTransaction); await transaction.RollbackAsync();
        }
        Assert.Null((await harness.Delivery.ReadSourceAsync(source)).Claim.Item.RecipientSnapshot);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Canonical_source_read_preserves_unavailable_cause_and_requested_cancellation(bool ef, bool providerCancellation)
    {
        await using var harness = await Harness.Create(ef); var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        Exception cause = providerCancellation ? new OperationCanceledException("Constructed unrequested provider cancellation.") : new IOException("Constructed canonical source read outage.");
        IStimulusDeliveryStore failed = ef ? new EfRuntimePostCommitOutboxStore(harness.Context!, new FailedAccess(cause)) :
            new InMemoryRuntimeCheckpointCommitStore(persistenceAccessContextAccessor: new FailedAccess(cause));
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => failed.ReadSourceAsync(source).AsTask());
        Assert.True(refusal.IsUnavailable); Assert.Same(cause, refusal.InnerException);
        var token = new CancellationToken(canceled: true);
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => failed.ReadSourceAsync(source, token).AsTask());
        Assert.Equal(token, cancelled.CancellationToken);
    }

    [Fact]
    public async Task Canonical_EF_source_read_rechecks_peer_takeover_after_the_actual_publisher_read()
    {
        var hook = new BeforePublisherRead(); await using var harness = await Harness.Create(true, hook);
        var stale = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        hook.Action = async () =>
        {
            var peer = (IRuntimePostCommitOutboxClaimStore)harness.Reopen();
            await peer.RecordDeliveryResultAsync(stale.Claim, new(stale.SourceOutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now, "retry"));
            harness.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Single(await peer.ClaimAsync(new("peer", Now.AddSeconds(2), TimeSpan.FromMinutes(1), 1)));
        };
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => harness.Delivery.ReadSourceAsync(stale).AsTask());
        Assert.Equal(1, hook.Interleavings); Assert.Equal("peer", (await harness.Lookup.FindAsync(stale.SourceOutboxItemId))!.DeliveringOwnerId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Canonical_EF_source_read_refuses_data_only_winner_or_removal_of_an_observed_winner(bool remove)
    {
        await using var harness = await Harness.Create(true); var original = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(original, Snapshot(original, true));
        var observed = await harness.Delivery.ReadSourceAsync(original);
        var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync(); var json = JsonNode.Parse(row.ContentJson)!;
        if (remove) json.AsObject().Remove("recipientSnapshot");
        else json["recipientSnapshot"]!.AsObject().Remove("retentionAcceptance");
        row.ContentJson = json.ToJsonString(); row.Revision++; await harness.Context.SaveChangesAsync();
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.ReadSourceAsync(remove ? observed : original).AsTask());
        Assert.False(refusal.IsUnavailable); Assert.IsType<InvalidDataException>(refusal.InnerException);
        Assert.Equal(row.ContentJson, (await harness.Context.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).ContentJson);
    }
}
