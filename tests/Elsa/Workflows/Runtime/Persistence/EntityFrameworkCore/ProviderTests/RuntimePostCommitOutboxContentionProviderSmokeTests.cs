using Elsa.Workflows.Runtime.Core.Contracts;
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
        RuntimePostCommitOutboxContentionSmoke.RunAsync(
            fixture,
            c => new BookmarkStatePostgreSqlDbContext(
                new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(c).Options));
}

internal static class RuntimePostCommitOutboxContentionSmoke
{
    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext)
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

    private static RuntimePostCommitOutboxItem Pending(
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

    private sealed class FixedScopeAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
