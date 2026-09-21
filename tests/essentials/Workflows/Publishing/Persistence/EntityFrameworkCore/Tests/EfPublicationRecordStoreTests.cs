using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfPublicationRecordStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Created = new(2026, 9, 15, 10, 0, 0, TimeSpan.FromHours(2));
    private SqliteTestDatabase database = null!;

    public async Task InitializeAsync() => database = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(Create);

    public async Task DisposeAsync() => await database.DisposeAsync();

    [Fact]
    public async Task Records_are_create_only_idempotent_and_survive_a_restart()
    {
        var record = Record("publication-1", "slot-1") with
        {
            Failure = new PublicationFailure("code", ""),
            Status = PublicationStatus.Failed
        };
        await using (var context = Open())
        {
            var store = Store(context, "tenant-a");
            await store.SaveAsync(record);
            await store.SaveAsync(record);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(record with { ArtifactId = "other-artifact" }).AsTask());
        }

        await using var restarted = Open();
        Assert.Equal(record, await Store(restarted, "tenant-a").FindAsync("publication-1"));
        Assert.Null(await Store(restarted, "tenant-a").FindAsync("missing"));
    }

    [Fact]
    public async Task A_transition_is_a_compare_and_swap_on_the_observed_status_and_never_changes_identity()
    {
        var candidate = Record("publication-cas", "slot-1");
        await using var context = Open();
        var store = Store(context, "tenant-a");
        await store.SaveAsync(candidate);

        var active = candidate with { Status = PublicationStatus.Active, ActivatedAt = Created.AddMinutes(1), SourceReferenceId = "reference-2" };
        Assert.False(await store.TryTransitionAsync(active, PublicationStatus.Active));
        Assert.True(await store.TryTransitionAsync(active, PublicationStatus.Candidate));
        Assert.False(await store.TryTransitionAsync(active with { Status = PublicationStatus.Retired }, PublicationStatus.Candidate));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.TryTransitionAsync(active with { SlotName = "other", Status = PublicationStatus.Retired }, PublicationStatus.Active).AsTask());
        Assert.False(await Store(context, "tenant-a").TryTransitionAsync(Record("missing", "slot-1"), PublicationStatus.Candidate));

        await using var restarted = Open();
        Assert.Equal(active, await Store(restarted, "tenant-a").FindAsync("publication-cas"));
    }

    [Fact]
    public async Task A_transition_losing_to_a_concurrent_writer_reports_false_and_keeps_the_winner()
    {
        var candidate = Record("publication-race", "slot-1");
        await using (var setup = Open())
            await Store(setup, "tenant-a").SaveAsync(candidate);

        var winner = candidate with { Status = PublicationStatus.Active, ActivatedAt = Created.AddMinutes(1) };
        var loser = candidate with { Status = PublicationStatus.Failed, Failure = new PublicationFailure("lost", "The slot moved.") };
        // The competing transition commits between the loser's read and its write.
        await using var contender = Open(new InterleaveBeforeSaveInterceptor(async () =>
        {
            await using var other = Open();
            Assert.True(await Store(other, "tenant-a").TryTransitionAsync(winner, PublicationStatus.Candidate));
        }));

        Assert.False(await Store(contender, "tenant-a").TryTransitionAsync(loser, PublicationStatus.Candidate));
        Assert.Empty(contender.ChangeTracker.Entries());
        await using var verify = Open();
        Assert.Equal(winner, await Store(verify, "tenant-a").FindAsync("publication-race"));
    }

    [Fact]
    public async Task Parallel_contexts_racing_one_transition_produce_exactly_one_winner()
    {
        var candidate = Record("publication-parallel", "slot-1");
        await using (var setup = Open())
            await Store(setup, "tenant-a").SaveAsync(candidate);

        var contenders = Enumerable.Range(0, 6).Select(index => Open()).ToArray();
        try
        {
            var results = await Task.WhenAll(contenders.Select((context, index) => Task.Run(() => Store(context, "tenant-a")
                .TryTransitionAsync(candidate with { Status = PublicationStatus.Active, ActivatedAt = Created.AddMinutes(index + 1) }, PublicationStatus.Candidate)
                .AsTask())));
            Assert.Single(results, result => result);
        }
        finally
        {
            foreach (var context in contenders)
                await context.DisposeAsync();
        }

        await using var verify = Open();
        Assert.Equal(PublicationStatus.Active, (await Store(verify, "tenant-a").FindAsync("publication-parallel"))!.Status);
    }

    [Fact]
    public async Task A_create_race_accepts_the_same_record_and_refuses_a_different_one()
    {
        var record = Record("publication-create-race", "slot-1");
        await using (var identical = Open(new InterleaveBeforeSaveInterceptor(async () =>
                     {
                         await using var other = Open();
                         await Store(other, "tenant-a").SaveAsync(record);
                     })))
            await Store(identical, "tenant-a").SaveAsync(record);

        var different = Record("publication-create-race-2", "slot-1");
        await using var conflicting = Open(new InterleaveBeforeSaveInterceptor(async () =>
        {
            await using var other = Open();
            await Store(other, "tenant-a").SaveAsync(different with { ArtifactId = "winner-artifact" });
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(conflicting, "tenant-a").SaveAsync(different).AsTask());
        Assert.Empty(conflicting.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Slot_listing_is_exhaustive_across_pages_scoped_and_ordered_by_creation_then_ordinal_id()
    {
        var records = Enumerable.Range(0, PublishingLedgerEfModule.SlotListPageSize + 44)
            .Select(index => Record($"publication-{index:D4}", "slot-paged") with { CreatedAt = Created.AddSeconds(index % 7) })
            .Append(Record("B", "slot-paged") with { CreatedAt = Created })
            .Append(Record("a", "slot-paged") with { CreatedAt = Created })
            .ToArray();
        await using (var context = Open())
        {
            var store = Store(context, "tenant-a");
            foreach (var record in records)
                await store.SaveAsync(record);
            await store.SaveAsync(Record("other-slot", "slot-other"));
            await Store(context, "tenant-b").SaveAsync(Record("foreign", "slot-paged"));
        }

        await using var restarted = Open();
        var listed = await Store(restarted, "tenant-a").ListBySlotAsync("slot-paged");
        var expected = records
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.PublicationId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, listed);
        Assert.Equal("B", listed.First().PublicationId);
        Assert.Single(await Store(restarted, "tenant-b").ListBySlotAsync("slot-paged"));
        Assert.Empty(await Store(restarted, "tenant-c").ListBySlotAsync("slot-paged"));
    }

    [Fact]
    public async Task Scopes_are_isolated_global_is_explicit_and_across_scopes_is_refused()
    {
        await using var context = Open();
        await Store(context, "tenant-a").SaveAsync(Record("same-id", "slot-1") with { ArtifactId = "artifact-a" });
        await Store(context, "tenant-b").SaveAsync(Record("same-id", "slot-1") with { ArtifactId = "artifact-b" });
        var global = new EfPublicationRecordStore(context, new TestAccess(PersistenceAccessContext.Global));
        await global.SaveAsync(Record("same-id", "slot-1") with { ArtifactId = "artifact-global" });

        Assert.Equal("artifact-a", (await Store(context, "tenant-a").FindAsync("same-id"))!.ArtifactId);
        Assert.Equal("artifact-b", (await Store(context, "tenant-b").FindAsync("same-id"))!.ArtifactId);
        Assert.Equal("artifact-global", (await global.FindAsync("same-id"))!.ArtifactId);
        var across = new EfPublicationRecordStore(context, new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("sweep"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => across.FindAsync("same-id").AsTask());
    }

    [Fact]
    public async Task Opaque_utf16_identities_and_failure_text_round_trip_losslessly()
    {
        const string tenant = "tenant-\0-\uD800";
        var record = new PublicationRecord(
            "publication-\0-\uD801",
            "slot-\0-\uD802",
            "definition-\0-\uD803",
            "version-\0-\uD804",
            "artifact-\0-\uD805",
            "reference-\0-\uD806",
            3,
            PublicationStatus.Failed,
            Created,
            null,
            Created.AddHours(1),
            new PublicationFailure("code-\0-\uD807", "message-\0-\uD808"),
            "name-\0-\uD809");
        await using (var context = Open())
        {
            await Store(context, tenant).SaveAsync(record);
            var row = await context.PublicationRecords.AsNoTracking().SingleAsync();
            Assert.Equal(EfRelationalIdentity.Encode(record.PublicationId), row.PublicationId);
            Assert.Equal(EfRelationalIdentity.Encode(tenant), row.TenantId);
        }

        await using var restarted = Open();
        var store = Store(restarted, tenant);
        Assert.Equal(record, await store.FindAsync(record.PublicationId));
        Assert.Equal(record, Assert.Single(await store.ListBySlotAsync(record.SlotId)));
    }

    [Fact]
    public async Task Drifted_or_malformed_rows_fail_closed()
    {
        await using var context = Open();
        var store = Store(context, "tenant-a");
        await store.SaveAsync(Record("drift-status", "slot-1"));
        await store.SaveAsync(Record("drift-residual", "slot-2"));
        await store.SaveAsync(Record("drift-slot-hash", "slot-3"));

        await context.PublicationRecords.Where(row => row.PublicationIdHash == EfRelationalIdentity.Hash("drift-status"))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.Status, "Promoted"));
        await context.PublicationRecords.Where(row => row.PublicationIdHash == EfRelationalIdentity.Hash("drift-residual"))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.PublicationId, EfRelationalIdentity.Encode("someone-else")));
        await context.PublicationRecords.Where(row => row.PublicationIdHash == EfRelationalIdentity.Hash("drift-slot-hash"))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.SlotId, EfRelationalIdentity.Encode("slot-forged")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync("drift-status").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync("drift-residual").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListBySlotAsync("slot-3").AsTask());
    }

    [Fact]
    public async Task A_failed_transition_is_not_committed_by_a_later_save_on_the_same_context()
    {
        await using var context = Open();
        var store = Store(context, "tenant-a");
        var candidate = Record("zombie", "slot-1");
        await store.SaveAsync(candidate);

        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TRIGGER reject_record_update BEFORE UPDATE ON {PublishingLedgerEfModule.PublicationRecordTableName} BEGIN SELECT RAISE(ABORT, 'transient'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.TryTransitionAsync(candidate with { Status = PublicationStatus.Active }, PublicationStatus.Candidate).AsTask());
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_record_update;");

        await store.SaveAsync(Record("unrelated", "slot-1"));
        await using var verify = Open();
        Assert.Equal(PublicationStatus.Candidate, (await Store(verify, "tenant-a").FindAsync("zombie"))!.Status);
    }

    internal static PublicationRecord Record(string publicationId, string slotId) => new(
        publicationId,
        slotId,
        "definition-1",
        "version-1",
        "artifact-1",
        "reference-1",
        0,
        PublicationStatus.Candidate,
        Created,
        null,
        null,
        null);

    private static PublishingSnapshotReviewSqliteDbContext Create(DbContextOptions<PublishingSnapshotReviewSqliteDbContext> options) => new(options);

    private PublishingSnapshotReviewSqliteDbContext Open(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        database.Open<PublishingSnapshotReviewSqliteDbContext>(Create, interceptors);

    private static EfPublicationRecordStore Store(PublishingSnapshotReviewDbContext context, string tenant) => new(context, TestAccess.Scoped(tenant));
}
