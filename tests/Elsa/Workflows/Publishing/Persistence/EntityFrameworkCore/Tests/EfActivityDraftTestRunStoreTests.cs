using Elsa.Activities.Design.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfActivityDraftTestRunStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.FromHours(1));
    private SqliteTestDatabase database = null!;

    public async Task InitializeAsync() => database = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(Create);

    public async Task DisposeAsync() => await database.DisposeAsync();

    [Fact]
    public async Task Receipts_are_create_only_found_by_idempotency_key_and_survive_a_restart()
    {
        var receipt = Receipt("tenant-a", "draft-1", "key-1");
        await using (var context = Open())
        {
            var store = Store(context, "tenant-a");
            var created = await store.TryCreateAsync(receipt);
            Assert.True(created.Created);
            var repeated = await store.TryCreateAsync(receipt with { Status = ActivityDraftTestRunReceiptStatus.Dispatching });
            Assert.False(repeated.Created);
            ReceiptAssert.Equivalent(receipt, repeated.Receipt);
        }

        await using var restarted = Open();
        var reopened = Store(restarted, "tenant-a");
        ReceiptAssert.Equivalent(receipt, await reopened.FindAsync(receipt.TestRunId));
        ReceiptAssert.Equivalent(receipt, await reopened.FindByIdempotencyKeyAsync(receipt.OperationScope, "draft-1", "key-1"));
        Assert.Null(await reopened.FindAsync("activity-test-run-missing"));
    }

    [Fact]
    public async Task Updates_compare_and_swap_the_revision_and_keep_the_request_identity()
    {
        var receipt = Receipt("tenant-a", "draft-1", "key-cas");
        await using var context = Open();
        var store = Store(context, "tenant-a");
        await store.TryCreateAsync(receipt);

        var dispatching = receipt with { Status = ActivityDraftTestRunReceiptStatus.Dispatching, ArtifactId = "artifact-1", Revision = 2 };
        Assert.Throws<ArgumentException>(() => store.TryUpdateAsync(dispatching, 0).AsTask().GetAwaiter().GetResult());
        Assert.False(await store.TryUpdateAsync(dispatching with { Revision = 3 }, 2));
        Assert.True(await store.TryUpdateAsync(dispatching, 1));
        Assert.False(await store.TryUpdateAsync(dispatching, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.TryUpdateAsync(dispatching with { DraftId = "other-draft", Revision = 3 }, 2).AsTask());
        Assert.False(await store.TryUpdateAsync(Receipt("tenant-a", "draft-1", "never-created") with { Revision = 2 }, 1));

        await using var restarted = Open();
        ReceiptAssert.Equivalent(dispatching, await Store(restarted, "tenant-a").FindAsync(receipt.TestRunId));
    }

    [Fact]
    public async Task An_update_losing_to_a_concurrent_writer_reports_false_and_keeps_the_winner()
    {
        var receipt = Receipt("tenant-a", "draft-1", "key-race");
        await using (var setup = Open())
            await Store(setup, "tenant-a").TryCreateAsync(receipt);

        var winner = receipt with { Status = ActivityDraftTestRunReceiptStatus.DispatchAccepted, Revision = 2 };
        await using var contender = Open(new InterleaveBeforeSaveInterceptor(async () =>
        {
            await using var other = Open();
            Assert.True(await Store(other, "tenant-a").TryUpdateAsync(winner, 1));
        }));

        Assert.False(await Store(contender, "tenant-a").TryUpdateAsync(receipt with { Status = ActivityDraftTestRunReceiptStatus.DispatchRejected, Revision = 2 }, 1));
        Assert.Empty(contender.ChangeTracker.Entries());
        await using var verify = Open();
        ReceiptAssert.Equivalent(winner, await Store(verify, "tenant-a").FindAsync(receipt.TestRunId));
    }

    [Fact]
    public async Task Parallel_updates_of_one_revision_have_exactly_one_winner()
    {
        var receipt = Receipt("tenant-a", "draft-1", "key-parallel");
        await using (var setup = Open())
            await Store(setup, "tenant-a").TryCreateAsync(receipt);

        var contenders = Enumerable.Range(0, 6).Select(_ => Open()).ToArray();
        try
        {
            var results = await Task.WhenAll(contenders.Select((context, index) => Task.Run(() => Store(context, "tenant-a")
                .TryUpdateAsync(receipt with { CommandDispatchStatus = $"attempt-{index}", Revision = 2 }, 1)
                .AsTask())));
            Assert.Single(results, result => result);
        }
        finally
        {
            foreach (var context in contenders)
                await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task Tenant_and_operation_scope_are_enforced_before_any_row_is_returned()
    {
        await using var context = Open();
        var receipt = Receipt("tenant-a", "draft-1", "key-tenant");
        await Store(context, "tenant-a").TryCreateAsync(receipt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context, "tenant-b").TryCreateAsync(receipt).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Store(context, "tenant-a").TryCreateAsync(receipt with { TestRunId = "activity-test-run-forged", OperationScope = "global" }).AsTask());
        Assert.Null(await Store(context, "tenant-b").FindAsync(receipt.TestRunId));
        var across = new EfActivityDraftTestRunStore(context, new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("sweep"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => across.FindAsync(receipt.TestRunId).AsTask());
    }

    [Fact]
    public async Task Expired_receipts_are_deleted_in_expiry_then_ordinal_order_within_the_limit_and_scope()
    {
        await using (var setup = Open())
        {
            var store = Store(setup, "tenant-a");
            await store.TryCreateAsync(Receipt("tenant-a", "draft-1", "late") with { ReceiptExpiresAt = Now.AddMinutes(-1) });
            await store.TryCreateAsync(Receipt("tenant-a", "draft-1", "early-z") with { ReceiptExpiresAt = Now.AddMinutes(-5) });
            await store.TryCreateAsync(Receipt("tenant-a", "draft-1", "early-a") with { ReceiptExpiresAt = Now.AddMinutes(-5).ToOffset(TimeSpan.FromHours(-7)) });
            await store.TryCreateAsync(Receipt("tenant-a", "draft-1", "live") with { ReceiptExpiresAt = Now.AddMinutes(5) });
            await Store(setup, "tenant-b").TryCreateAsync(Receipt("tenant-b", "draft-1", "foreign") with { ReceiptExpiresAt = Now.AddMinutes(-9) });
        }

        var earlyIds = new[] { "early-z", "early-a" }
            .Select(key => Receipt("tenant-a", "draft-1", key).TestRunId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await using var context = Open();
        var sweeper = Store(context, "tenant-a");
        Assert.Equal(1, await sweeper.DeleteExpiredAsync(Now, 1));
        Assert.Null(await sweeper.FindAsync(earlyIds[0]));
        Assert.NotNull(await sweeper.FindAsync(earlyIds[1]));

        Assert.Equal(2, await sweeper.DeleteExpiredAsync(Now, 10));
        Assert.Equal(0, await sweeper.DeleteExpiredAsync(Now, 10));
        Assert.NotNull(await sweeper.FindAsync(Receipt("tenant-a", "draft-1", "live").TestRunId));
        Assert.NotNull(await Store(context, "tenant-b").FindAsync(Receipt("tenant-b", "draft-1", "foreign").TestRunId));
        Assert.Throws<ArgumentOutOfRangeException>(() => sweeper.DeleteExpiredAsync(Now, 0).AsTask().GetAwaiter().GetResult());
    }

    [Fact]
    public async Task A_sweep_racing_an_update_skips_the_newer_revision()
    {
        var receipt = Receipt("tenant-a", "draft-1", "key-sweep-race") with { ReceiptExpiresAt = Now.AddMinutes(-1) };
        await using (var setup = Open())
            await Store(setup, "tenant-a").TryCreateAsync(receipt);

        // The update lands after the sweep selected its candidates and before it deletes them.
        var extended = receipt with { ReceiptExpiresAt = Now.AddMinutes(-1), Revision = 2, Status = ActivityDraftTestRunReceiptStatus.Dispatching };
        await using var context = Open(new DeleteInterleaveInterceptor(async () =>
        {
            await using var other = Open();
            Assert.True(await Store(other, "tenant-a").TryUpdateAsync(extended, 1));
        }));

        Assert.Equal(0, await Store(context, "tenant-a").DeleteExpiredAsync(Now, 10));
        await using var verify = Open();
        ReceiptAssert.Equivalent(extended, await Store(verify, "tenant-a").FindAsync(receipt.TestRunId));
    }

    [Fact]
    public async Task Failure_diagnostics_and_opaque_text_round_trip_losslessly()
    {
        const string tenant = "tenant-\0-\uD800";
        var receipt = Receipt(tenant, "draft-\uD801", "key-\0") with
        {
            Status = ActivityDraftTestRunReceiptStatus.ValidationRejected,
            Failure = new ActivityDraftTestRunFailure(
                ActivityDraftTestRunFailureKind.Validation,
                "code-\uD802",
                "message-\0",
                [new ActivityDiagnostic("diagnostic", ActivityDiagnosticSeverity.Error, "bad-\uD803", new ActivityDiagnosticSubject("ActivityDraft", "draft-\uD801"))])
        };
        await using (var context = Open())
            Assert.True((await Store(context, tenant).TryCreateAsync(receipt)).Created);

        await using var restarted = Open();
        ReceiptAssert.Equivalent(receipt, await Store(restarted, tenant).FindAsync(receipt.TestRunId));
    }

    [Fact]
    public async Task Drifted_projections_fail_closed()
    {
        await using var context = Open();
        var store = Store(context, "tenant-a");
        var expiry = Receipt("tenant-a", "draft-1", "drift-expiry");
        var revision = Receipt("tenant-a", "draft-1", "drift-revision");
        await store.TryCreateAsync(expiry);
        await store.TryCreateAsync(revision);

        await context.ActivityDraftTestRuns.Where(row => row.TestRunId == EfRelationalIdentity.Encode(expiry.TestRunId))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.ReceiptExpiresAtUtcTicks, Now.AddYears(1).UtcTicks));
        await context.ActivityDraftTestRuns.Where(row => row.TestRunId == EfRelationalIdentity.Encode(revision.TestRunId))
            .ExecuteUpdateAsync(row => row.SetProperty(x => x.Revision, 7L));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(expiry.TestRunId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(revision.TestRunId).AsTask());
    }

    internal static ActivityDraftTestRunReceipt Receipt(string? tenantId, string draftId, string key)
    {
        var operationScope = ActivityDraftTestRunIdentity.CreateOperationScope(tenantId);
        return new ActivityDraftTestRunReceipt(
            ActivityDraftTestRunIdentity.CreateTestRunId(operationScope, draftId, key),
            operationScope,
            ActivityDraftTestRunIdentity.HashIdempotencyKey(key),
            draftId,
            3,
            "definition-1",
            tenantId,
            tenantId,
            "sha256:request",
            ActivityDraftTestRunIdentity.CreateWorkflowExecutionId(operationScope, draftId, key),
            ActivityDraftTestRunReceiptStatus.Preparing,
            null,
            null,
            null,
            null,
            Now,
            Now,
            Now.AddHours(1),
            Now.AddDays(1),
            ActivityDraftTestRunCancellationStatus.Unavailable,
            1);
    }

    private static PublishingSnapshotReviewSqliteDbContext Create(DbContextOptions<PublishingSnapshotReviewSqliteDbContext> options) => new(options);

    private PublishingSnapshotReviewSqliteDbContext Open(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        database.Open<PublishingSnapshotReviewSqliteDbContext>(Create, interceptors);

    private static EfActivityDraftTestRunStore Store(PublishingSnapshotReviewDbContext context, string tenant) => new(context, TestAccess.Scoped(tenant));

    /// <summary>Runs a competing write once, just before the sweep's first conditional delete.</summary>
    private sealed class DeleteInterleaveInterceptor(Func<Task> interleave) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int remaining = 1;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref remaining, 0) == 1)
                await interleave();
            return result;
        }
    }
}
