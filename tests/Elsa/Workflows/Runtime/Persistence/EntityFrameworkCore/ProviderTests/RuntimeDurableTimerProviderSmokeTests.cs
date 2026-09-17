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
public sealed class RuntimeDurableTimerPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_durable_timer_smoke() => RuntimeDurableTimerProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStatePostgreSqlDbContext(
            new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
        BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeDurableTimerSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_durable_timer_smoke() => RuntimeDurableTimerProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateSqlServerDbContext(
            new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
        BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeDurableTimerMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_durable_timer_smoke() => RuntimeDurableTimerProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateMySqlDbContext(
            new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
        BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeDurableTimerProviderSmoke
{
    private const string SigningKey = "ef-runtime-r24-native-provider-signing-key-32-bytes";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r24-{Guid.NewGuid():N}";
        var connectionString = fixture.ConnectionString;
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var claimScope = $"{scope}-claims";
        RuntimeDurableTimerClaim initialClaim;

        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);

            foreach (var timerId in new[] { "c", "a", "b" })
                await store.SaveAsync(Timer("workflow-order", timerId, now));
            await store.SaveAsync(Timer("workflow-order", "future", now.AddTicks(1)));

            var due = await store.ListDueAsync(now, 2);
            Assert.Equal(new[] { "a", "b" }, due.Select(timer => timer.TimerId));
            var firstPage = await store.ListPageAsync(new DurableTimerPageQuery("workflow-order", 2));
            Assert.Equal(new[] { "a", "b" }, firstPage.Items.Select(timer => timer.TimerId));
            Assert.NotNull(firstPage.NextContinuationToken);

            await using var transaction = await context.Database.BeginTransactionAsync();
            await store.SaveAsync(Timer("workflow-rollback", "timer", now));
            await transaction.RollbackAsync();
        }

        await using (var restarted = createContext(connectionString))
        {
            var store = Store(restarted, scope);
            Assert.Equal(now, (await store.FindAsync("workflow-order", "a"))!.DueTime);
            Assert.Null(await store.FindAsync("workflow-rollback", "timer"));

            var claimStore = Store(restarted, claimScope);
            await claimStore.SaveAsync(Timer("workflow-claim", "timer", now));
            initialClaim = Assert.Single(await claimStore.ClaimDueAsync(new RuntimeDurableTimerClaimRequest(
                "worker-a", now, TimeSpan.FromMinutes(1), 1)));
        }

        await using (var recovered = createContext(connectionString))
        {
            var claimStore = Store(recovered, claimScope);
            Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded,
                (await claimStore.ReleaseClaimAsync(initialClaim, initialClaim.VisibleAfter)).Status);
            var recoveredClaim = Assert.Single(await claimStore.ClaimDueAsync(new RuntimeDurableTimerClaimRequest(
                "worker-b", initialClaim.VisibleAfter, TimeSpan.FromMinutes(1), 1)));
            Assert.True(recoveredClaim.FencingToken > initialClaim.FencingToken);
            Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Stale,
                (await claimStore.CompleteClaimAsync(initialClaim)).Status);
            Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded,
                (await claimStore.CompleteClaimAsync(recoveredClaim)).Status);
            Assert.Null(await claimStore.FindAsync("workflow-claim", "timer"));
        }
    }

    private static EfDurableTimerStore Store(BookmarkStateDbContext context, string scope) =>
        new(
            context,
            new FixedAccessor(scope),
            new HmacRuntimeRecoveryContinuationCodec(
                Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));

    private static DurableTimer Timer(string workflowExecutionId, string timerId, DateTimeOffset dueTime) =>
        new(timerId, workflowExecutionId, "Delay", $"stimulus-{timerId}", dueTime, dueTime.AddMinutes(-1));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
