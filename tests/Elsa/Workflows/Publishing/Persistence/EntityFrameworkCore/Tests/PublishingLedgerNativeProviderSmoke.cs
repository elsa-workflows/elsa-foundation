using System.Data.Common;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.ActivityPublicationTestMaterial;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>The three module contexts an ordered reusable-activity publication commits through, on one native provider.</summary>
/// <remarks>
/// <see cref="CreateDesignDatabase"/> exists for PostgreSQL alone: the Activities Design model declares the database
/// collation <c>C</c>, which <c>EnsureCreated</c> cannot request from a server whose template database uses another
/// collation, so the database is created from <c>template0</c> first, as a host provisioning it would.
/// </remarks>
internal sealed record PublishingNativeProvider(
    Func<string, IInterceptor[], PublishingSnapshotReviewDbContext> Publishing,
    Func<string, IInterceptor[], ActivitiesDesignDbContext> Design,
    Func<string, IInterceptor[], BookmarkStateDbContext> Runtime,
    Func<string, string, Task>? CreateDesignDatabase = null)
{
    public static PublishingNativeProvider PostgreSql { get; } = new(
        (connection, interceptors) => new PublishingSnapshotReviewPostgreSqlDbContext(Options<PublishingSnapshotReviewPostgreSqlDbContext>(builder => builder.UseNpgsql(connection), interceptors)),
        (connection, interceptors) => new ActivitiesDesignPostgreSqlDbContext(Options<ActivitiesDesignPostgreSqlDbContext>(builder => builder.UseNpgsql(connection), interceptors)),
        (connection, interceptors) => new BookmarkStatePostgreSqlDbContext(Options<BookmarkStatePostgreSqlDbContext>(builder => builder.UseNpgsql(connection), interceptors)),
        async (serverConnection, database) =>
        {
            await using var connection = new NpgsqlConnection(serverConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{database}\" TEMPLATE template0 LC_COLLATE 'C'";
            await command.ExecuteNonQueryAsync();
        });

    public static PublishingNativeProvider SqlServer { get; } = new(
        (connection, interceptors) => new PublishingSnapshotReviewSqlServerDbContext(Options<PublishingSnapshotReviewSqlServerDbContext>(builder => builder.UseSqlServer(connection), interceptors)),
        (connection, interceptors) => new ActivitiesDesignSqlServerDbContext(Options<ActivitiesDesignSqlServerDbContext>(builder => builder.UseSqlServer(connection), interceptors)),
        (connection, interceptors) => new BookmarkStateSqlServerDbContext(Options<BookmarkStateSqlServerDbContext>(builder => builder.UseSqlServer(connection), interceptors)));

    public static PublishingNativeProvider MySql { get; } = new(
        (connection, interceptors) => new PublishingSnapshotReviewMySqlDbContext(Options<PublishingSnapshotReviewMySqlDbContext>(builder => builder.UseMySQL(connection), interceptors)),
        (connection, interceptors) => new ActivitiesDesignMySqlDbContext(Options<ActivitiesDesignMySqlDbContext>(builder => builder.UseMySQL(connection), interceptors)),
        (connection, interceptors) => new BookmarkStateMySqlDbContext(Options<BookmarkStateMySqlDbContext>(builder => builder.UseMySQL(connection), interceptors)));

    private static DbContextOptions<TContext> Options<TContext>(Action<DbContextOptionsBuilder<TContext>> use, IInterceptor[] interceptors)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        use(builder);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return builder.Options;
    }
}

/// <summary>
/// Native-provider round trips for the P01, P05 and P06 ledger stores and for the ADR 0066 ordered publication,
/// against each provider's real collation, uniqueness and concurrency behaviour. Every race holds its contenders
/// until all have read, so each one is a genuine write-write conflict the provider has to arbitrate.
/// </summary>
internal static class PublishingLedgerNativeProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.FromHours(2));

    public static async Task RunLedgerAsync(string connectionString, PublishingNativeProvider provider)
    {
        var prefix = $"ledger-{Guid.NewGuid():N}";
        var tenant = $"{prefix}-tenant";
        var opaqueTenant = $"{prefix}-\0-\uD800";
        PublishingSnapshotReviewDbContext Open(params IInterceptor[] interceptors) => provider.Publishing(connectionString, interceptors);
        await using (var setup = Open())
            await setup.Database.EnsureCreatedAsync();

        // P01: ids that differ only by case stay distinct under every collation, opaque UTF-16 survives, a slot
        // lists exhaustively past one keyset page, and one transition wins a race.
        var upper = EfPublicationRecordStoreTests.Record($"{prefix}-Publication", "slot-case");
        var lower = EfPublicationRecordStoreTests.Record($"{prefix}-publication", "slot-case");
        var opaque = EfPublicationRecordStoreTests.Record($"{prefix}-\0-\uD801", "slot-\0-\uD802") with
        {
            Status = PublicationStatus.Failed,
            RetiredAt = Now,
            Failure = new PublicationFailure("code-\uD803", "")
        };
        const int pagedCount = PublishingLedgerEfModule.SlotListPageSize + 4;
        await using (var context = Open())
        {
            var records = new EfPublicationRecordStore(context, TestAccess.Scoped(tenant));
            await records.SaveAsync(upper);
            await records.SaveAsync(upper);
            await records.SaveAsync(lower);
            await records.SaveAsync(opaque);
            await Assert.ThrowsAsync<InvalidOperationException>(() => records.SaveAsync(upper with { ArtifactId = "other-artifact" }).AsTask());
            for (var index = 0; index < pagedCount; index++)
                await records.SaveAsync(EfPublicationRecordStoreTests.Record($"{prefix}-paged-{index:D3}", "slot-paged") with { CreatedAt = Now.AddSeconds(-(index % 5)) });
        }

        var transitioned = await RaceAsync(Open,
            [upper with { Status = PublicationStatus.Active, ActivatedAt = Now }, upper with { Status = PublicationStatus.Retired, RetiredAt = Now }],
            (context, transition) => new EfPublicationRecordStore(context, TestAccess.Scoped(tenant)).TryTransitionAsync(transition, PublicationStatus.Candidate).AsTask());

        // P05: one create wins a race, and opaque receipt material survives under an opaque tenant.
        var receipt = EfActivityPublicationReceiptStoreTests.Receipt(tenant, $"{prefix}-key");
        var opaqueReceipt = EfActivityPublicationReceiptStoreTests.Receipt(opaqueTenant, $"{prefix}-key-\0-\uD804") with
        {
            ReviewToken = "review-\0-\uD805",
            Diagnostics = [new ActivityDiagnostic("code-\uD806", ActivityDiagnosticSeverity.Warning, "message-\0", new ActivityDiagnosticSubject("Kind", "id-\uD807"))]
        };
        var createdReceipt = await RaceAsync(Open,
            [receipt, receipt with { ReviewToken = "sha256:other-review" }],
            (context, candidate) => new EfActivityPublicationReceiptStore(context, TestAccess.Scoped(tenant)).TryCreateAsync(candidate).AsTask());
        await using (var context = Open())
            Assert.True(await new EfActivityPublicationReceiptStore(context, TestAccess.Scoped(opaqueTenant)).TryCreateAsync(opaqueReceipt));

        // P06: create-only receipts, one compare-and-swap winner, and ordered, bounded, scoped expiry cleanup.
        var live = EfActivityDraftTestRunStoreTests.Receipt(tenant, "draft-1", $"{prefix}-live") with { ReceiptExpiresAt = Now.AddHours(1) };
        var expiredEarly = EfActivityDraftTestRunStoreTests.Receipt(tenant, "draft-1", $"{prefix}-expired-early") with { ReceiptExpiresAt = Now.AddMinutes(-10) };
        var expiredLate = EfActivityDraftTestRunStoreTests.Receipt(tenant, "draft-1", $"{prefix}-expired-late") with { ReceiptExpiresAt = Now.AddMinutes(-1) };
        var expiredForeign = EfActivityDraftTestRunStoreTests.Receipt(opaqueTenant, "draft-\uD808", $"{prefix}-foreign-\0") with { ReceiptExpiresAt = Now.AddMinutes(-20) };
        await using (var context = Open())
        {
            var testRuns = new EfActivityDraftTestRunStore(context, TestAccess.Scoped(tenant));
            Assert.True((await testRuns.TryCreateAsync(live)).Created);
            Assert.False((await testRuns.TryCreateAsync(live)).Created);
            Assert.True((await testRuns.TryCreateAsync(expiredLate)).Created);
            Assert.True((await testRuns.TryCreateAsync(expiredEarly)).Created);
            Assert.True((await new EfActivityDraftTestRunStore(context, TestAccess.Scoped(opaqueTenant)).TryCreateAsync(expiredForeign)).Created);
        }

        var dispatching = live with { Status = ActivityDraftTestRunReceiptStatus.Dispatching, Revision = 2 };
        var updated = await RaceAsync(Open,
            [dispatching, dispatching with { Status = ActivityDraftTestRunReceiptStatus.DispatchRejected }],
            (context, update) => new EfActivityDraftTestRunStore(context, TestAccess.Scoped(tenant)).TryUpdateAsync(update, 1).AsTask());
        await using (var context = Open())
        {
            var testRuns = new EfActivityDraftTestRunStore(context, TestAccess.Scoped(tenant));
            Assert.Equal(1, await testRuns.DeleteExpiredAsync(Now, 1));
            Assert.Null(await testRuns.FindAsync(expiredEarly.TestRunId));
            Assert.NotNull(await testRuns.FindAsync(expiredLate.TestRunId));
            Assert.Equal(1, await testRuns.DeleteExpiredAsync(Now, 10));
            Assert.Equal(0, await testRuns.DeleteExpiredAsync(Now, 10));
        }

        await using var restarted = Open();
        var reopenedRecords = new EfPublicationRecordStore(restarted, TestAccess.Scoped(tenant));
        Assert.Equal(lower, await reopenedRecords.FindAsync(lower.PublicationId));
        Assert.Equal(opaque, await reopenedRecords.FindAsync(opaque.PublicationId));
        Assert.Equal(transitioned, await reopenedRecords.FindAsync(upper.PublicationId));
        Assert.Equal(2, (await reopenedRecords.ListBySlotAsync("slot-case")).Count);
        var paged = await reopenedRecords.ListBySlotAsync("slot-paged");
        Assert.Equal(pagedCount, paged.Select(record => record.PublicationId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(paged.OrderBy(record => record.CreatedAt).ThenBy(record => record.PublicationId, StringComparer.Ordinal), paged);
        Assert.Empty(await new EfPublicationRecordStore(restarted, TestAccess.Scoped(opaqueTenant)).ListBySlotAsync("slot-paged"));

        var reopenedReceipts = new EfActivityPublicationReceiptStore(restarted, TestAccess.Scoped(tenant));
        ReceiptAssert.Equivalent(createdReceipt, await reopenedReceipts.FindAsync(tenant, receipt.IdempotencyKey));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopenedReceipts.FindAsync(opaqueTenant, opaqueReceipt.IdempotencyKey).AsTask());
        ReceiptAssert.Equivalent(opaqueReceipt, await new EfActivityPublicationReceiptStore(restarted, TestAccess.Scoped(opaqueTenant)).FindAsync(opaqueTenant, opaqueReceipt.IdempotencyKey));

        var reopenedTestRuns = new EfActivityDraftTestRunStore(restarted, TestAccess.Scoped(tenant));
        ReceiptAssert.Equivalent(updated, await reopenedTestRuns.FindAsync(live.TestRunId));
        Assert.Null(await reopenedTestRuns.FindAsync(expiredLate.TestRunId));
        // The tenant's sweep never reached the other scope's even older receipt.
        ReceiptAssert.Equivalent(expiredForeign, await new EfActivityDraftTestRunStore(restarted, TestAccess.Scoped(opaqueTenant)).FindAsync(expiredForeign.TestRunId));
    }

    /// <summary>
    /// Each module gets its own database on the provider, as on a host that splits the lanes, so no phase can
    /// lean on a connection or transaction another module's context opened.
    /// </summary>
    public static async Task RunOrderedPublicationAsync(string connectionString, PublishingNativeProvider provider)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var publishing = WithDatabase(connectionString, $"elsa_ledger_{suffix}_pub");
        var design = WithDatabase(connectionString, $"elsa_ledger_{suffix}_design");
        var runtime = WithDatabase(connectionString, $"elsa_ledger_{suffix}_rt");
        if (provider.CreateDesignDatabase is not null)
            await provider.CreateDesignDatabase(connectionString, $"elsa_ledger_{suffix}_design");
        await using (var context = provider.Publishing(publishing, []))
            await context.Database.EnsureCreatedAsync();
        await using (var context = provider.Runtime(runtime, []))
            await context.Database.EnsureCreatedAsync();
        await using (var context = provider.Design(design, []))
        {
            await context.Database.EnsureCreatedAsync();
            await SeedDraftAsync(context, "definition-1", "draft-1");
            await SeedDraftAsync(context, "definition-2", "draft-2");
            await SeedDraftAsync(context, "definition-3", "draft-3");
        }

        ActivityPublicationScope Open(IInterceptor[]? publishingInterceptors = null, IInterceptor[]? runtimeInterceptors = null) =>
            new(provider.Publishing(publishing, publishingInterceptors ?? []), provider.Design(design, []), provider.Runtime(runtime, runtimeInterceptors ?? []), TestAccess.Scoped("default"));

        // Interrupted between the design commit and the receipt, then resumed by replaying the same commit.
        var commit = Commit();
        var crash = new RefuseSaveInterceptor(context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added));
        await using (var crashed = Open([crash]))
            await Assert.ThrowsAsync<ActivityPublicationReceiptPendingException>(() => crashed.Command.ExecuteAsync(commit));
        await using (var inspect = Open())
        {
            Assert.NotNull(await ((IActivityDefinitionVersionPublicationStore)inspect.DesignStores).FindAsync("version-1"));
            Assert.Null(await inspect.Receipts.FindAsync(null, commit.Receipt.IdempotencyKey));
        }

        await using (var retry = Open())
            Assert.Equal("version-1", (await retry.Command.ExecuteAsync(commit)).DefinitionVersionId);
        await using (var replay = Open())
            await Assert.ThrowsAsync<InvalidOperationException>(() => replay.Command.ExecuteAsync(commit));

        // Two attempts of one publication, both past their receipt check, converge on one committed publication.
        var raced = Commit("definition-2", "draft-2", "version-2", hashCharacter: 'c', idempotencyKey: "raced-operation", sourceReferenceId: "source-ref-2");
        var rendezvous = new RendezvousAfterFirstQueryInterceptor.Rendezvous(2);
        await using (var first = Open([new RendezvousAfterFirstQueryInterceptor(rendezvous)]))
        await using (var second = Open([new RendezvousAfterFirstQueryInterceptor(rendezvous)]))
        {
            var results = await Task.WhenAll(
                Task.Run(() => first.Command.ExecuteAsync(raced)),
                Task.Run(() => second.Command.ExecuteAsync(raced)));
            Assert.All(results, result => Assert.Equal("version-2", result.DefinitionVersionId));
        }

        await using (var verify = Open())
        {
            ReceiptAssert.Equivalent(commit.Receipt, await verify.Receipts.FindAsync(null, commit.Receipt.IdempotencyKey));
            ReceiptAssert.Equivalent(raced.Receipt, await verify.Receipts.FindAsync(null, raced.Receipt.IdempotencyKey));
            Assert.Equal(2, await verify.Design.ActivityDefinitionVersionPublications.AsNoTracking().CountAsync());
            Assert.Equal(2, (await verify.Design.ActivityDependencyProjections.AsNoTracking().SingleAsync()).Sequence);
            Assert.Equal(ActivityDefinitionDraftStatus.Published, (await ((IActivityDefinitionDraftStore)verify.DesignStores).FindAsync("draft-2"))!.Status);
            Assert.NotNull(await verify.Templates.FindAsync(raced.ExecutableTemplate.TemplateId));
            Assert.NotNull(await verify.SourceReferences.FindAsync("source-ref-2"));
        }

        // A racing attempt commits only the Runtime phase between this attempt's template id read and its hash
        // claim read. Under read-committed isolation that interleaving is real, and the identical material must be
        // adopted rather than reported as a hash collision or a claim without its row.
        var interleaved = Commit("definition-3", "draft-3", "version-3", hashCharacter: 'd', idempotencyKey: "interleaved-operation", sourceReferenceId: "source-ref-3");
        var interleave = new BeforeFirstReadInterceptor(RuntimeArtifactEfModule.ExecutableActivityTemplateHashClaimTableName, async () =>
        {
            await using var racing = Open();
            Assert.True(await new EfActivityPublicationRuntimeCommit(racing.Templates, racing.SourceReferences)
                .CommitAsync(interleaved.ExecutableTemplate, interleaved.SourceReference));
        });
        await using (var overtaken = Open(runtimeInterceptors: [interleave]))
            Assert.Equal("version-3", (await overtaken.Command.ExecuteAsync(interleaved)).DefinitionVersionId);
        Assert.True(interleave.Fired);

        await using (var source = Open())
            await source.SourceCommand.ExecuteAsync(SourceCommit(Published));
        await using var sourceVerify = Open();
        ReceiptAssert.Equivalent(interleaved.Receipt, await sourceVerify.Receipts.FindAsync(null, interleaved.Receipt.IdempotencyKey));
        Assert.NotNull(await ((IActivityDefinitionVersionPublicationStore)sourceVerify.DesignStores).FindAsync("source-version-1"));
        Assert.Equal("source-version-1", (await ((IActivityDefinitionAuthoringStore)sourceVerify.DesignStores).FindAsync("source-definition-1"))!.HeadVersionId);
    }

    /// <summary>Races one write per candidate, each in its own context, and returns the single candidate that won.</summary>
    private static async Task<T> RaceAsync<T>(
        Func<IInterceptor[], PublishingSnapshotReviewDbContext> open,
        T[] candidates,
        Func<PublishingSnapshotReviewDbContext, T, Task<bool>> attempt)
    {
        var rendezvous = new RendezvousAfterFirstQueryInterceptor.Rendezvous(candidates.Length);
        var contexts = candidates.Select(_ => open([new RendezvousAfterFirstQueryInterceptor(rendezvous)])).ToArray();
        try
        {
            var results = await Task.WhenAll(candidates.Select((candidate, index) => Task.Run(() => attempt(contexts[index], candidate))));
            Assert.Single(results, won => won);
            return candidates[Array.IndexOf(results, true)];
        }
        finally
        {
            foreach (var context in contexts)
                await context.DisposeAsync();
        }
    }

    private static string WithDatabase(string connectionString, string database)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        builder.Remove("Initial Catalog");
        builder.Remove("Database");
        builder["Database"] = database;
        return builder.ConnectionString;
    }
}
