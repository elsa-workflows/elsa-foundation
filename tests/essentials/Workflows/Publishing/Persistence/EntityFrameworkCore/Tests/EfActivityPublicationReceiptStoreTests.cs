using Elsa.Activities.Design.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfActivityPublicationReceiptStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Updated = new(2026, 9, 15, 10, 0, 0, TimeSpan.FromHours(-5));
    private SqliteTestDatabase database = null!;

    public async Task InitializeAsync() => database = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(Create);

    public async Task DisposeAsync() => await database.DisposeAsync();

    [Fact]
    public async Task Receipts_are_create_only_never_replaced_and_survive_a_restart()
    {
        var receipt = Receipt("tenant-a", "key-1");
        await using (var context = Open())
        {
            var store = Store(context, "tenant-a");
            Assert.True(await store.TryCreateAsync(receipt));
            Assert.False(await store.TryCreateAsync(receipt with { Status = ActivityPublicationReceiptStatus.Failed, ErrorCode = "later" }));
        }

        await using var restarted = Open();
        var loaded = await Store(restarted, "tenant-a").FindAsync("tenant-a", "key-1");
        ReceiptAssert.Equivalent(receipt, loaded);
        Assert.Equal(ActivityPublicationReceiptStatus.Applied, loaded!.Status);
        Assert.Equal(Updated, loaded.UpdatedAt);
        Assert.Equal(Updated.Offset, loaded.UpdatedAt.Offset);
        Assert.Null(await Store(restarted, "tenant-a").FindAsync("tenant-a", "missing"));
    }

    [Fact]
    public async Task Another_tenant_is_refused_before_disclosure_and_an_untenanted_receipt_is_a_different_key()
    {
        await using var context = Open();
        Assert.True(await Store(context, "tenant-a").TryCreateAsync(Receipt("tenant-a", "shared-key")));
        Assert.True(await Store(context, "tenant-a").TryCreateAsync(Receipt(null, "shared-key")));
        Assert.True(await Store(context, "tenant-b").TryCreateAsync(Receipt("tenant-b", "shared-key")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context, "tenant-a").FindAsync("tenant-b", "shared-key").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context, "tenant-a").TryCreateAsync(Receipt("tenant-b", "other-key")).AsTask());
        Assert.Equal("tenant-a", (await Store(context, "tenant-a").FindAsync("tenant-a", "shared-key"))!.TenantId);
        Assert.Null((await Store(context, "tenant-a").FindAsync(null, "shared-key"))!.TenantId);
        Assert.Null(await Store(context, "tenant-b").FindAsync(null, "shared-key"));
        Assert.Equal(3, await context.ActivityPublicationReceipts.CountAsync());
    }

    [Fact]
    public async Task Parallel_creates_of_one_key_have_exactly_one_winner()
    {
        var contenders = Enumerable.Range(0, 6).Select(_ => Open()).ToArray();
        try
        {
            var results = await Task.WhenAll(contenders.Select((context, index) => Task.Run(() =>
                Store(context, "tenant-a").TryCreateAsync(Receipt("tenant-a", "raced") with { ReviewToken = $"review-{index}" }).AsTask())));
            Assert.Single(results, result => result);
        }
        finally
        {
            foreach (var context in contenders)
                await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_create_losing_between_its_check_and_its_insert_reports_false_and_leaves_nothing_tracked()
    {
        await using var contender = Open(new InterleaveBeforeSaveInterceptor(async () =>
        {
            await using var other = Open();
            Assert.True(await Store(other, "tenant-a").TryCreateAsync(Receipt("tenant-a", "interleaved")));
        }));

        Assert.False(await Store(contender, "tenant-a").TryCreateAsync(Receipt("tenant-a", "interleaved") with { ReviewToken = "loser" }));
        Assert.Empty(contender.ChangeTracker.Entries());
        Assert.Equal("sha256:review", (await Store(contender, "tenant-a").FindAsync("tenant-a", "interleaved"))!.ReviewToken);
    }

    [Fact]
    public async Task Opaque_utf16_receipt_material_round_trips_losslessly()
    {
        const string tenant = "tenant-\0-\uD800";
        var receipt = Receipt(tenant, "key-\0-\uD801") with
        {
            ReviewToken = "review-\0-\uD802",
            Diagnostics =
            [
                new ActivityDiagnostic("code-\uD803", ActivityDiagnosticSeverity.Warning, "message-\0", new ActivityDiagnosticSubject("Kind", "id-\uD804"),
                    Metadata: new Dictionary<string, string> { ["key-\uD805"] = "value-\0" })
            ]
        };
        await using (var context = Open())
        {
            Assert.True(await Store(context, tenant).TryCreateAsync(receipt));
            var row = await context.ActivityPublicationReceipts.AsNoTracking().SingleAsync();
            Assert.DoesNotContain('\uD800', row.Content);
            Assert.Equal(EfRelationalIdentity.Encode(tenant), row.ReceiptTenantId);
        }

        await using var restarted = Open();
        var loaded = await Store(restarted, tenant).FindAsync(tenant, receipt.IdempotencyKey);
        ReceiptAssert.Equivalent(receipt, loaded);
        Assert.Equal("value-\0", loaded!.Diagnostics.Single().Metadata["key-\uD805"]);
    }

    [Fact]
    public async Task Drifted_receipt_material_fails_closed()
    {
        await using var context = Open();
        var store = Store(context, "tenant-a");
        await store.TryCreateAsync(Receipt("tenant-a", "drift-status"));
        await store.TryCreateAsync(Receipt("tenant-a", "drift-content"));
        await store.TryCreateAsync(Receipt("tenant-a", "drift-schema"));

        var foreignContent = await context.ActivityPublicationReceipts.AsNoTracking()
            .Where(row => row.IdempotencyKey == EfRelationalIdentity.Encode("drift-status"))
            .Select(row => row.Content)
            .SingleAsync();
        await context.ActivityPublicationReceipts.Where(row => row.IdempotencyKey == EfRelationalIdentity.Encode("drift-status"))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.Status, nameof(ActivityPublicationReceiptStatus.Rejected)));
        await context.ActivityPublicationReceipts.Where(row => row.IdempotencyKey == EfRelationalIdentity.Encode("drift-content"))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.Content, foreignContent));
        await context.ActivityPublicationReceipts.Where(row => row.IdempotencyKey == EfRelationalIdentity.Encode("drift-schema"))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.SchemaVersion, "2"));

        // Damaged material: the row claims a version this build writes, so its contents should have been
        // internally consistent and are not. That is corruption, and still reports as corruption.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync("tenant-a", "drift-status").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync("tenant-a", "drift-content").AsTask());

        // A version this build does not run wrote this row. Nothing here is damaged, so reporting it as
        // corruption sent an operator looking for data damage that does not exist (ADR 0077, #1950).
        var skew = await Assert.ThrowsAsync<EfSchemaVersionSkewException>(
            () => store.FindAsync("tenant-a", "drift-schema").AsTask());
        Assert.Equal("PublishingLedger", skew.Module);
        Assert.Equal("2", skew.Found);
        Assert.Equal(PublishingLedgerEfModule.ContentSchemaVersion, skew.Expected);
    }

    internal static ActivityPublicationReceipt Receipt(string? tenantId, string key) => new(
        tenantId,
        key,
        "sha256:fingerprint",
        ActivityPublicationReceiptStatus.Applied,
        "draft-1",
        4,
        null,
        "sha256:review",
        "1.0.0",
        new ActivityPublicationOutcome("definition-1", "version-1", "draft-1", "1.0.0", "template-1", "sha256:template", "reference-1", Updated),
        null,
        [],
        Updated);

    private static PublishingSnapshotReviewSqliteDbContext Create(DbContextOptions<PublishingSnapshotReviewSqliteDbContext> options) => new(options);

    private PublishingSnapshotReviewSqliteDbContext Open(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        database.Open<PublishingSnapshotReviewSqliteDbContext>(Create, interceptors);

    private static EfActivityPublicationReceiptStore Store(PublishingSnapshotReviewDbContext context, string tenant) => new(context, TestAccess.Scoped(tenant));
}
