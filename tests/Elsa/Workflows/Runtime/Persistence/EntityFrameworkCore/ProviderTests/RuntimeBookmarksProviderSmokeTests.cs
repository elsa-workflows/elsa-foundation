using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class RuntimeBookmarksProviderSmokeTests
{
    [SkippableFact]
    public Task SqlServer_crud_query_and_model_concurrency_smoke() => RunAsync("EF_RUNTIME_BOOKMARKS_SQLSERVER_CONNECTION_STRING", (connection, _) =>
        new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options));

    [SkippableFact]
    public Task PostgreSql_crud_query_and_model_concurrency_smoke() => RunAsync("EF_RUNTIME_BOOKMARKS_POSTGRESQL_CONNECTION_STRING", (connection, _) =>
        new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options));

    [SkippableFact]
    public Task MySql_crud_query_and_model_concurrency_smoke() => RunAsync("EF_RUNTIME_BOOKMARKS_MYSQL_CONNECTION_STRING", (connection, _) =>
        new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options));

    private static async Task RunAsync(string variable, Func<string, string, BookmarkStateDbContext> factory)
    {
        var connection = Environment.GetEnvironmentVariable(variable);
        Skip.If(string.IsNullOrWhiteSpace(connection), $"Set {variable} to run the native provider bookmark smoke.");
        var database = $"{connection};";
        await using var context = factory(database, variable);
        await context.Database.EnsureCreatedAsync();
        var store = new EfBookmarkStateStore(context, new Accessor("provider-smoke"));
        var bookmark = new BookmarkState("bm-1", "wf-1", "act-1", "node-1", "resume-1", "Event", "hash-1", JsonSerializer.SerializeToElement(new { ok = true }), new Dictionary<string, string>(), DateTimeOffset.UtcNow, null);
        await store.SaveAsync(bookmark);
        await using var concurrentContext = factory(database, variable);
        var stale = await concurrentContext.Bookmarks.SingleAsync();
        await store.SaveAsync(bookmark with { StimulusHash = "hash-2" });
        stale.MetadataJson = "{}";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => concurrentContext.SaveChangesAsync());
        concurrentContext.ChangeTracker.Clear();
        Assert.Equal("bm-1", (await store.FindAsync("wf-1", "bm-1"))!.BookmarkId);
        Assert.Single((await store.ListPageAsync(new BookmarkStatePageQuery("wf-1", 1))).Items);
        Assert.True(await store.DeleteAsync("wf-1", "bm-1"));
    }

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
