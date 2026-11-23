using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.PostCommitOutboxClaimedListingContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="PostCommitOutboxClaimedListingContract"/> on the in-memory checkpoint store and on the EF Core outbox store
/// over SQLite. The native-provider smoke test runs it on PostgreSQL.
/// </summary>
public sealed class PostCommitOutboxClaimedListingContractTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private RuntimeSqliteDbContext? _context;

    [Fact]
    public Task The_in_memory_store_lists_one_executions_held_items_of_one_kind()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        return ListsOneExecutionsHeldItemsOfOneKindAsync(new Backend(store, store, item => store.AddPendingForTestingAsync(item)));
    }

    [Fact]
    public async Task The_entity_framework_store_lists_one_executions_held_items_of_one_kind()
    {
        await ListsOneExecutionsHeldItemsOfOneKindAsync(await EntityFrameworkAsync());
    }

    [Fact]
    public Task The_in_memory_store_also_lists_the_deliveries_a_claim_finished_since_a_time()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        return AlsoListsTheDeliveriesAClaimFinishedSinceAsync(new Backend(store, store, item => store.AddPendingForTestingAsync(item)));
    }

    [Fact]
    public async Task The_entity_framework_store_also_lists_the_deliveries_a_claim_finished_since_a_time()
    {
        await AlsoListsTheDeliveriesAClaimFinishedSinceAsync(await EntityFrameworkAsync());
    }

    [Fact]
    public Task The_in_memory_store_excludes_observed_deliveries_before_the_provider_page_limit()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        return ExcludesObservedDeliveriesBeforeTheProviderPageLimitAsync(new Backend(store, store, item => store.AddPendingForTestingAsync(item)));
    }

    [Fact]
    public async Task The_entity_framework_store_excludes_observed_deliveries_before_the_provider_page_limit()
    {
        await ExcludesObservedDeliveriesBeforeTheProviderPageLimitAsync(await EntityFrameworkAsync());
    }

    [Fact]
    public async Task The_entity_framework_store_excludes_delivered_ids_ordinally_under_a_case_insensitive_collation()
    {
        await ExcludesObservedDeliveriesWithOrdinalIdentityAsync(await EntityFrameworkAsync(caseInsensitiveIdentities: true));
    }

    /// <summary>
    /// A row last written before the claim delivery time was projected has none, so it reads and is simply not listed. A
    /// projected time that differs from the row's content is corrupt and refused like every other outbox projection.
    /// </summary>
    [Fact]
    public async Task The_entity_framework_store_lists_no_legacy_claim_delivery_and_refuses_a_corrupt_one()
    {
        var backend = await EntityFrameworkAsync();
        var at = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        const string kind = RuntimePostCommitIntentKinds.EnqueueSchedulerWork;
        await backend.AddPendingAsync(new RuntimePostCommitOutboxItem(
            "delivered",
            new RuntimePostCommitIntent("intent-delivered", "wf-draining", kind, at, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            at,
            availableAt: at,
            retryPolicy: RuntimePostCommitRetryPolicy.None));
        var claim = Assert.Single(await backend.Claims.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "sweep", at, TimeSpan.FromMinutes(1), limit: 1, "wf-draining", kind)));
        await backend.Claims.RecordDeliveryResultAsync(claim, new RuntimePostCommitOutboxDeliveryResult(
            claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, at));
        var query = new RuntimePostCommitOutboxClaimedQuery("wf-draining", kind, limit: 10, deliveredByClaimSince: at);
        Assert.Equal(["delivered"], (await backend.Claims.ListClaimedAsync(query)).Select(item => item.OutboxItemId));

        await ProjectClaimDeliveryAsync(null);
        Assert.Empty(await backend.Claims.ListClaimedAsync(query));

        await ProjectClaimDeliveryAsync(at.AddTicks(1).UtcTicks);
        await Assert.ThrowsAsync<InvalidDataException>(() => backend.Claims.ListClaimedAsync(query).AsTask());
    }

    private async Task ProjectClaimDeliveryAsync(long? ticks)
    {
        var row = await _context!.RuntimePostCommitOutbox.SingleAsync();
        row.ClaimDeliveredAtUtcTicks = ticks;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private async Task<Backend> EntityFrameworkAsync(bool caseInsensitiveIdentities = false)
    {
        await _connection.OpenAsync();
        if (caseInsensitiveIdentities)
            _connection.CreateCollation("BINARY", (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left, right));
        _context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(_connection).Options);
        await _context.Database.EnsureCreatedAsync();
        return PostCommitOutboxClaimedListingContract.EntityFramework(_context, "tenant-a");
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
