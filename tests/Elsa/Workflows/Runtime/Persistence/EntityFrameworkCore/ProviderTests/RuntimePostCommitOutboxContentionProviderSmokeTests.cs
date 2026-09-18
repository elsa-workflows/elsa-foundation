using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>
/// Issue #1798 on a real database. Two deliverers contend for one outbox row, on two connections, exactly as the live
/// drain and the resumption sweep do in a running host.
///
/// This lane exists because SQLite cannot show the defect: its writes are serialized, which masks precisely the
/// interleaving that produced the reported HTTP 500 on PostgreSQL. Every pre-existing fencing test in the repository
/// serializes its two claimers on one thread, which is why this survived a store migration.
/// </summary>
[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimePostCommitOutboxContentionPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_claim_less_recording_of_a_claimed_item_reports_superseded() =>
        RuntimePostCommitOutboxContentionSmoke.RunAsync(fixture, CreateContext);

    [SkippableFact]
    public Task PostgreSql_claimed_completion_that_lost_its_fence_surfaces_a_stale_claim() =>
        RuntimePostCommitOutboxClaimedCompletionContentionSmoke.RunAsync(fixture, CreateContext);

    private static RuntimeDbContext CreateContext(string connectionString) =>
        new RuntimePostgreSqlDbContext(
            new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connectionString).Options);
}

internal static class RuntimePostCommitOutboxContentionSmoke
{
    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");

        var scope = $"contention-{Guid.NewGuid():N}";
        var connectionString = fixture.ConnectionString;
        var now = DateTimeOffset.UtcNow;
        var outboxItemId = $"outbox-{Guid.NewGuid():N}";

        await using var setupContext = createContext(connectionString);
        await setupContext.Database.EnsureCreatedAsync();
        var setupStore = new EfRuntimePostCommitOutboxStore(setupContext, new FixedScopeAccessor(scope));
        await setupStore.SavePendingAsync(Pending(outboxItemId, "workflow-contention", now));

        // Two deliverers, two DbContexts, two connections - the shape a live drain and the resumption sweep have in a
        // running host, where each resolves its own scoped store from its own DI scope.
        await using var sweepContext = createContext(connectionString);
        await using var drainContext = createContext(connectionString);
        var sweepStore = new EfRuntimePostCommitOutboxStore(sweepContext, new FixedScopeAccessor(scope));
        var drainStore = new EfRuntimePostCommitOutboxStore(drainContext, new FixedScopeAccessor(scope));

        // The live drain reads its deliverable work...
        var drainItems = await drainStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(now, 10));
        Assert.Contains(drainItems, item => item.OutboxItemId == outboxItemId);

        // ...and the sweep claims it before the drain gets to record. This is the whole defect.
        var claim = Assert.Single(await sweepStore.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("sweep-owner", now, TimeSpan.FromMinutes(1), 10)));
        Assert.Equal(outboxItemId, claim.OutboxItemId);

        // Before the fix this threw InvalidOperationException out of the store, escaped the drain orchestrator, and
        // became a 500 on workflow start. It must now report the loss instead.
        var outcome = await drainStore.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId, RuntimePostCommitOutboxStatus.Delivered, now.AddSeconds(1)));

        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner, outcome);

        // Nothing was written: the sweep's claim is intact, so its own completion still succeeds rather than being
        // rejected as stale. That is the property that makes tolerating the race safe rather than merely quiet.
        await using var verifyContext = createContext(connectionString);
        var verifyStore = new EfRuntimePostCommitOutboxStore(verifyContext, new FixedScopeAccessor(scope));
        var current = await verifyStore.FindAsync(outboxItemId);
        Assert.NotNull(current);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, current.Status);
        Assert.Equal("sweep-owner", current.DeliveringOwnerId);
        Assert.Equal(claim.FencingToken, current.DeliveryFencingToken);
        Assert.Equal(0, current.DeliveryAttemptCount);

        var completion = await sweepStore.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(
                outboxItemId, RuntimePostCommitOutboxStatus.Delivered, now.AddSeconds(2)),
            null,
            null));

        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, completion);
    }

    internal static RuntimePostCommitOutboxItem Pending(
        string outboxItemId,
        string workflowExecutionId,
        DateTimeOffset recordedAt) => new(
        outboxItemId,
        new RuntimePostCommitIntent(
            $"intent-{outboxItemId}",
            workflowExecutionId,
            "provider-smoke.outbox",
            recordedAt,
            null,
            null,
            null),
        RuntimePostCommitOutboxStatus.Pending,
        recordedAt,
        recordedAt);

    internal sealed class FixedScopeAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}

/// <summary>
/// Issue #1812: the other half of the same race, on a real database. Here the losing deliverer holds a <b>claim</b>, so
/// the store's answer is deliberately not the one <see cref="RuntimePostCommitOutboxContentionSmoke"/> asserts. A
/// claim-less recording reports <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner"/>
/// because contention is inherent when there is no claim to lose; a claimant that lost its fence broke the lease it was
/// given, and <see cref="IRuntimePostCommitOutboxClaimCompletionStore"/> requires it be told so by exception.
///
/// The interleaving matters. The claim and the completion run on <b>one</b> store instance, which is how the processor
/// drives them: one scoped store per cycle claims, dispatches, then completes. The row is therefore already tracked from
/// the claim, so the completion validates the claim against its own pre-race snapshot, passes that check, and loses only
/// on the row's concurrency token inside SaveChanges. That late-detection branch is the one that had no test at any
/// layer.
/// </summary>
internal static class RuntimePostCommitOutboxClaimedCompletionContentionSmoke
{
    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");

        var scope = $"contention-{Guid.NewGuid():N}";
        var connectionString = fixture.ConnectionString;
        var now = DateTimeOffset.UtcNow;
        var outboxItemId = $"outbox-{Guid.NewGuid():N}";
        var visibilityTimeout = TimeSpan.FromMinutes(1);

        await using var setupContext = createContext(connectionString);
        await setupContext.Database.EnsureCreatedAsync();
        var setupStore = new EfRuntimePostCommitOutboxStore(
            setupContext,
            new RuntimePostCommitOutboxContentionSmoke.FixedScopeAccessor(scope));
        await setupStore.SavePendingAsync(
            RuntimePostCommitOutboxContentionSmoke.Pending(outboxItemId, "workflow-contention", now));

        await using var drainContext = createContext(connectionString);
        await using var sweepContext = createContext(connectionString);
        var drainStore = new EfRuntimePostCommitOutboxStore(
            drainContext,
            new RuntimePostCommitOutboxContentionSmoke.FixedScopeAccessor(scope));
        var sweepStore = new EfRuntimePostCommitOutboxStore(
            sweepContext,
            new RuntimePostCommitOutboxContentionSmoke.FixedScopeAccessor(scope));

        // The drain claims the item and starts delivering it...
        var drainClaim = Assert.Single(await drainStore.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("drain-owner", now, visibilityTimeout, 10)));
        Assert.Equal(outboxItemId, drainClaim.OutboxItemId);

        // ...its delivery outruns the visibility timeout, so the sweep legitimately re-claims the item at a higher
        // fence. Nothing here is a fault - this is the lease doing its job.
        var expired = now.Add(visibilityTimeout).AddSeconds(1);
        var sweepClaim = Assert.Single(await sweepStore.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("sweep-owner", expired, visibilityTimeout, 10)));
        Assert.Equal(outboxItemId, sweepClaim.OutboxItemId);
        Assert.True(sweepClaim.FencingToken > drainClaim.FencingToken);

        // The drain now completes a claim it no longer holds. Its own snapshot still says it owns the item, so the loss
        // surfaces on the concurrency token inside SaveChanges; the store rolls back, re-reads and re-runs the
        // transition so a late-detected race raises exactly the exception an early-detected one raises.
        var stale = await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
            drainStore.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                drainClaim,
                new RuntimePostCommitOutboxDeliveryResult(
                    outboxItemId, RuntimePostCommitOutboxStatus.Delivered, expired.AddSeconds(1)),
                null,
                null)).AsTask());

        Assert.Equal(outboxItemId, stale.OutboxItemId);
        Assert.Equal("drain-owner", stale.PresentedOwnerId);
        Assert.Equal(drainClaim.FencingToken, stale.PresentedFencingToken);
        Assert.Equal("sweep-owner", stale.CurrentOwnerId);
        Assert.Equal(sweepClaim.FencingToken, stale.CurrentFencingToken);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, stale.CurrentStatus);

        // The refusal wrote nothing: the transaction rolled back, so the sweep's claim is intact and its own completion
        // still succeeds. Refusing the stale completion loses no delivery record - the owning deliverer still writes one.
        await using var verifyContext = createContext(connectionString);
        var verifyStore = new EfRuntimePostCommitOutboxStore(
            verifyContext,
            new RuntimePostCommitOutboxContentionSmoke.FixedScopeAccessor(scope));
        var current = await verifyStore.FindAsync(outboxItemId);
        Assert.NotNull(current);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, current.Status);
        Assert.Equal("sweep-owner", current.DeliveringOwnerId);
        Assert.Equal(sweepClaim.FencingToken, current.DeliveryFencingToken);
        Assert.Equal(0, current.DeliveryAttemptCount);

        var completion = await sweepStore.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            sweepClaim,
            new RuntimePostCommitOutboxDeliveryResult(
                outboxItemId, RuntimePostCommitOutboxStatus.Delivered, expired.AddSeconds(2)),
            null,
            null));

        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, completion);

        await using var finalContext = createContext(connectionString);
        var finalStore = new EfRuntimePostCommitOutboxStore(
            finalContext,
            new RuntimePostCommitOutboxContentionSmoke.FixedScopeAccessor(scope));
        var delivered = await finalStore.FindAsync(outboxItemId);
        Assert.NotNull(delivered);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
    }
}
