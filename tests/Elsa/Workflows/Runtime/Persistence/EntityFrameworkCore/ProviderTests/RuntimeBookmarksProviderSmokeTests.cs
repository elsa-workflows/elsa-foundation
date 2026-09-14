using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeBookmarksPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_live_schema_crud_ordered_query_rollback_concurrency_and_reopen() =>
        RuntimeBookmarksProviderSmoke.RunAsync(
            fixture,
            "PostgreSql",
            (connection, interceptor) => new BookmarkStatePostgreSqlDbContext(
                new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>()
                    .UseNpgsql(connection)
                    .AddInterceptors(interceptor is null ? [] : [interceptor])
                    .Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeBookmarksSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_live_schema_crud_ordered_query_rollback_concurrency_and_reopen() =>
        RuntimeBookmarksProviderSmoke.RunAsync(
            fixture,
            "SqlServer",
            (connection, interceptor) => new BookmarkStateSqlServerDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>()
                    .UseSqlServer(connection)
                    .AddInterceptors(interceptor is null ? [] : [interceptor])
                    .Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeBookmarksMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_live_schema_crud_ordered_query_rollback_concurrency_and_reopen() =>
        RuntimeBookmarksProviderSmoke.RunAsync(
            fixture,
            "MySql",
            (connection, interceptor) => new BookmarkStateMySqlDbContext(
                new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>()
                    .UseMySQL(connection)
                    .AddInterceptors(interceptor is null ? [] : [interceptor])
                    .Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeBookmarksProviderSmoke
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, IInterceptor?, BookmarkStateDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var connectionString = fixture.ConnectionString;

        var scope = $"provider-scope-{Guid.NewGuid():N}";
        var workflowId = $"provider-workflow-{Guid.NewGuid():N}";
        var bookmarkId = $"provider-bookmark-{Guid.NewGuid():N}";

        await using (var context = createContext(connectionString, null))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);

            var bookmark = Bookmark(workflowId, bookmarkId, "Event", "hash-a");
            await store.SaveAsync(bookmark);
            var roundTrip = await store.FindAsync(workflowId, bookmarkId);
            Assert.NotNull(roundTrip);
            Assert.Equal(bookmark.BookmarkId, roundTrip!.BookmarkId);
            Assert.Equal(bookmark.WorkflowExecutionId, roundTrip.WorkflowExecutionId);
            Assert.Equal(bookmark.StimulusHash, roundTrip.StimulusHash);
            Assert.Equal(bookmark.Payload!.Value.GetRawText(), roundTrip.Payload!.Value.GetRawText());
            Assert.Equal(bookmark.Metadata, roundTrip.Metadata);
            Assert.Equal(bookmark.CreatedAt, roundTrip.CreatedAt);

            foreach (var id in new[] { "c", "a", "b" })
                await store.SaveAsync(Bookmark(workflowId, id, "Event", id));
            var firstPage = await store.ListPageAsync(new BookmarkStatePageQuery(workflowId, 2));
            Assert.Equal(new[] { "a", "b" }, firstPage.Items.Select(item => item.BookmarkId));
            Assert.NotNull(firstPage.NextContinuationToken);
            var secondPage = await store.ListPageAsync(new BookmarkStatePageQuery(workflowId, 2, firstPage.NextContinuationToken));
            Assert.Equal(new[] { "c", bookmarkId }, secondPage.Items.Select(item => item.BookmarkId));
            Assert.Null(secondPage.NextContinuationToken);

            Assert.True(await store.DeleteAsync(workflowId, bookmarkId));
            Assert.Null(await store.FindAsync(workflowId, bookmarkId));
        }

        var rollbackWorkflowId = $"provider-rollback-workflow-{Guid.NewGuid():N}";
        await using (var transactionContext = createContext(connectionString, null))
        {
            await using var transaction = await transactionContext.Database.BeginTransactionAsync();
            await Store(transactionContext, scope).SaveAsync(Bookmark(rollbackWorkflowId, "rollback", "Event", "rollback"));
            await transaction.RollbackAsync();
        }

        await using (var rollbackVerificationContext = createContext(connectionString, null))
            Assert.Null(await Store(rollbackVerificationContext, scope).FindAsync(rollbackWorkflowId, "rollback"));

        var concurrencyWorkflowId = $"provider-concurrency-workflow-{Guid.NewGuid():N}";
        await using (var seedContext = createContext(connectionString, null))
            await Store(seedContext, scope).SaveAsync(Bookmark(concurrencyWorkflowId, "concurrency", "Event", "one"));

        var barrier = new ConcurrentSaveBarrier(2);
        await using (var leftContext = createContext(connectionString, barrier))
        await using (var rightContext = createContext(connectionString, barrier))
        {
            var outcomes = await Task.WhenAll(
                Capture(Store(leftContext, scope).SaveAsync(Bookmark(concurrencyWorkflowId, "concurrency", "Event", "left"))),
                Capture(Store(rightContext, scope).SaveAsync(Bookmark(concurrencyWorkflowId, "concurrency", "Event", "right"))));
            Assert.Single(outcomes, outcome => outcome is null);
            Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, outcome => outcome is not null));
        }

        await using var reopenedContext = createContext(connectionString, null);
        var reopened = await Store(reopenedContext, scope).FindAsync(concurrencyWorkflowId, "concurrency");
        Assert.NotNull(reopened);
        Assert.Contains(reopened!.StimulusHash, new[] { "left", "right" });
    }

    private static EfBookmarkStateStore Store(BookmarkStateDbContext context, string scope) =>
        new(context, new FixedAccessor(scope));

    private static async Task<Exception?> Capture(ValueTask<BookmarkState> operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static BookmarkState Bookmark(string workflowId, string bookmarkId, string stimulusType, string stimulusHash) => new(
        bookmarkId,
        workflowId,
        "activity-1",
        "node-1",
        "resume-1",
        stimulusType,
        stimulusHash,
        JsonSerializer.SerializeToElement(new { provider = true, id = bookmarkId }),
        new Dictionary<string, string> { ["provider"] = "smoke" },
        CreatedAt,
        null);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class ConcurrentSaveBarrier(int participants) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int remaining = participants;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref remaining) == 0)
                release.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
