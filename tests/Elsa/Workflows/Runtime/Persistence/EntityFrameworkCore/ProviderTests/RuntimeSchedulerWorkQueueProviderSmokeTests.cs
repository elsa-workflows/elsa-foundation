using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeSchedulerWorkQueuePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_scheduler_work_queue_smoke() => RuntimeSchedulerWorkQueueProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
        BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeSchedulerWorkQueueSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_scheduler_work_queue_smoke() => RuntimeSchedulerWorkQueueProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
        BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeSchedulerWorkQueueMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_scheduler_work_queue_smoke() => RuntimeSchedulerWorkQueueProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
        BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeSchedulerWorkQueueProviderSmoke
{
    private const string SigningKey = "ef-runtime-r22-native-provider-signing-key-32-bytes";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r22-{Guid.NewGuid():N}";
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        RuntimeSchedulerWorkClaim initialClaim;

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);
            foreach (var sequence in new[] { 3L, 1L, 2L })
                await store.EnqueueAsync(Work("workflow-order", $"work-{sequence}", sequence));
            var first = await store.ListAsync(new RuntimeSchedulerWorkQuery("workflow-order", 2));
            Assert.Equal(new long?[] { 1L, 2L }, first.Items.Select(item => item.Sequence));
            Assert.NotNull(first.NextContinuationToken);

            await using var transaction = await context.Database.BeginTransactionAsync();
            await store.EnqueueAsync(Work("workflow-rollback", "work", 1));
            await transaction.RollbackAsync();
        }

        await using (var restarted = createContext(fixture.ConnectionString))
        {
            var store = Store(restarted, scope);
            var second = await store.ListAsync(new RuntimeSchedulerWorkQuery(
                "workflow-order",
                2,
                (await store.ListAsync(new RuntimeSchedulerWorkQuery("workflow-order", 2))).NextContinuationToken));
            Assert.Equal(new long?[] { 3L }, second.Items.Select(item => item.Sequence));
            Assert.Empty((await store.ListAsync(new RuntimeSchedulerWorkQuery("workflow-rollback"))).Items);

            await store.EnqueueAsync(Work("workflow-claim", "work", 1));
            initialClaim = (await store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                "workflow-claim", "owner-a", now, TimeSpan.FromMinutes(1))))!;
            var renewed = (await store.RenewClaimAsync(initialClaim, now, TimeSpan.FromMinutes(2))).Claim!;
            Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale,
                (await store.ReleaseClaimAsync(initialClaim, now)).Status);
            Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
                (await store.ReleaseClaimAsync(renewed, renewed.VisibleAfter)).Status);
        }

        await using (var recovered = createContext(fixture.ConnectionString))
        {
            var store = Store(recovered, scope);
            var successor = (await store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                "workflow-claim", "owner-b", now.AddMinutes(2), TimeSpan.FromMinutes(1))))!;
            Assert.True(successor.FencingToken > initialClaim.FencingToken);
            Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale,
                (await store.CompleteClaimAsync(initialClaim)).Status);
            Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
                (await store.CompleteClaimAsync(successor)).Status);
            Assert.Empty((await store.ListAsync(new RuntimeSchedulerWorkQuery("workflow-claim"))).Items);
        }
    }

    private static EfSchedulerWorkQueueStore Store(BookmarkStateDbContext context, string scope) =>
        new(context, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));

    private static RuntimeSchedulerWorkItem Work(string workflowExecutionId, string workItemId, long sequence) =>
        new(workItemId, workflowExecutionId, $"command-{workItemId}", WorkflowExecutionCommandKind.ScheduleActivity,
            $"envelope-{workItemId}", $"idempotency-{workItemId}",
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), sequence);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
