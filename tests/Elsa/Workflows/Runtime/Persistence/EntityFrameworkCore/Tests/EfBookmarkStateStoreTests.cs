using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfBookmarkStateStoreTests
{
    [Fact]
    public async Task Saves_round_trips_composite_identity_scope_and_lossless_payload()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var state = State("wf-1", "same", "HttpEndpoint", "hash-a");
        await fixture.Store.SaveAsync(state);
        await fixture.Store.SaveAsync(State("wf-2", "same", "HttpEndpoint", "hash-b"));

        var result = await fixture.Store.FindAsync("wf-1", "same");
        Assert.NotNull(result);
        Assert.Equal("/orders", result!.Payload!.Value.GetProperty("path").GetString());
        Assert.Equal("é", result.Payload.Value.GetProperty("value").GetString());
        Assert.Equal(state.Metadata, result.Metadata);
        Assert.Equal(state.CreatedAt, result.CreatedAt);
        Assert.Equal(state.ExpiresAt, result.ExpiresAt);
        Assert.Equal("hash-b", (await fixture.Store.FindAsync("wf-2", "same"))!.StimulusHash);

        await using var otherScope = await fixture.ReopenAsync("tenant-b");
        Assert.Null(await otherScope.Store.FindAsync("wf-1", "same"));
    }

    [Fact]
    public async Task Pages_are_bounded_forward_and_stably_ordered_for_workflow_and_stimulus()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        foreach (var id in new[] { "c", "a", "b" })
            await fixture.Store.SaveAsync(State("wf-1", id, "Event", "same"));
        await fixture.Store.SaveAsync(State("wf-2", "a", "Event", "same"));

        var first = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-1", 2));
        Assert.Equal(new[] { "a", "b" }, first.Items.Select(x => x.BookmarkId));
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-1", 2, first.NextContinuationToken));
        Assert.Equal(new[] { "c" }, second.Items.Select(x => x.BookmarkId));
        Assert.Null(second.NextContinuationToken);

        var index = (IBookmarkStimulusIndex)fixture.Store;
        var stimulus = await index.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "same", 2));
        Assert.Equal(new[] { "wf-1:a", "wf-1:b" }, stimulus.Items.Select(x => $"{x.WorkflowExecutionId}:{x.BookmarkId}"));
        var type = await index.ListByStimulusTypePageAsync(new BookmarkStimulusTypePageQuery("Event", 2));
        Assert.Equal(new[] { "wf-1:a", "wf-1:b" }, type.Items.Select(x => $"{x.WorkflowExecutionId}:{x.BookmarkId}"));
    }

    [Fact]
    public async Task Paging_uses_ordinal_unicode_order_even_when_ids_share_prefixes()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        foreach (var id in new[] { "a\0\0", "a", "Z", "a\0" })
            await fixture.Store.SaveAsync(State("wf-ordinal", id, "Event", id));

        var page = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-ordinal", 10));

        Assert.Equal(new[] { "Z", "a", "a\0", "a\0\0" }, page.Items.Select(x => x.BookmarkId));
    }

    [Fact]
    public async Task Upsert_find_delete_and_restart_preserve_exact_identity()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "Event", "one"));
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "Timer", "two"));
        Assert.Equal("Timer", (await fixture.Store.FindAsync("wf-1", "bm-1"))!.StimulusType);
        Assert.True(await fixture.Store.DeleteAsync("wf-1", "bm-1"));
        Assert.False(await fixture.Store.DeleteAsync("wf-1", "bm-1"));

        await using var reopened = await fixture.ReopenAsync("tenant-a");
        Assert.Null(await reopened.Store.FindAsync("wf-1", "bm-1"));
    }

    [Fact]
    public async Task Accepts_contract_maximum_identity_and_projection_lengths()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var workflow = new string('w', 128);
        var bookmark = new string('b', 128);
        var stimulusType = new string('t', 256);
        var stimulusHash = new string('h', 450);
        var metadataKey = new string('m', 450);
        await fixture.Store.SaveAsync(State(workflow, bookmark, stimulusType, stimulusHash, new Dictionary<string, string> { [metadataKey] = "value" }));

        var result = await fixture.Store.FindAsync(workflow, bookmark);
        Assert.Equal(stimulusHash, result!.StimulusHash);
        Assert.Equal("value", result.Metadata[metadataKey]);
    }

    [Fact]
    public async Task Rejects_invalid_inputs_and_continuations_before_database_access()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.FindAsync(" ", "x").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.FindAsync(new string('w', 129), "x").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListByStimulusPageAsync(new BookmarkStimulusPageQuery(new string('t', 257), "hash")).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf", 1, "not-base64")).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf", 501)).AsTask());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Store.FindAsync("wf", "x", cancelled.Token).AsTask());
    }

    [Fact]
    public async Task Rejects_privileged_scoped_access_before_database_access()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var privilegedStore = new EfBookmarkStateStore(
            fixture.Context,
            new Accessor(PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), new PersistenceAccessPurpose("maintenance"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => privilegedStore.FindAsync("wf", "bookmark").AsTask());
    }

    [Fact]
    public async Task Clears_tracker_after_a_concurrency_failure_and_can_reuse_the_context()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new FaultingBookmarkStateDbContext(new DbContextOptionsBuilder<FaultingBookmarkStateDbContext>().UseSqlite(connection).Options);
        await using (context)
        {
            await context.Database.EnsureCreatedAsync();
            var store = new EfBookmarkStateStore(context, new Accessor("tenant-a"));
            context.FailNextSave = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(State("wf", "bookmark", "Event", "hash")).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
            await store.SaveAsync(State("wf", "bookmark", "Event", "hash"));
            Assert.NotNull(await store.FindAsync("wf", "bookmark"));
        }
    }

    [Fact]
    public async Task Corrupt_projection_fails_closed_instead_of_disappearing_from_lookup()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        var state = State("wf-1", "bm-1", "Event", "hash");
        await fixture.Store.SaveAsync(state);
        var id = await fixture.Context.Bookmarks.Select(row => row.Id).SingleAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_runtime_bookmark_state SET StimulusType = 'Corrupt' WHERE Id = {0}", id);
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("wf-1", "bm-1").AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => ((IBookmarkStimulusIndex)fixture.Store).ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "hash")).AsTask());
    }

    [Fact]
    public async Task Both_public_interfaces_resolve_to_the_same_scoped_instance()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeBookmarksEntityFrameworkCore(new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfBookmarkStateStore>(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>(), scope.ServiceProvider.GetRequiredService<IBookmarkStimulusIndex>());
    }

    private static BookmarkState State(string workflow, string bookmark, string stimulusType, string hash, Dictionary<string, string>? metadata = null) => new(
        bookmark, workflow, "activity-1", "node-1", "resume-1", stimulusType, hash,
        JsonSerializer.SerializeToElement(new { path = "/orders", value = "é" }),
        metadata ?? new Dictionary<string, string> { ["tag"] = "one", ["unicode"] = "é" },
        new DateTimeOffset(2026, 9, 13, 10, 15, 16, 123, TimeSpan.FromHours(2)),
        new DateTimeOffset(2026, 9, 14, 10, 15, 16, 123, TimeSpan.FromHours(-5)));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string scope;
        public BookmarkStateSqliteDbContext Context { get; }
        public EfBookmarkStateStore Store { get; }

        private Fixture(SqliteConnection connection, string scope, BookmarkStateSqliteDbContext context)
        {
            this.connection = connection;
            this.scope = scope;
            Context = context;
            Store = new EfBookmarkStateStore(context, new Accessor(scope));
        }

        public static async Task<Fixture> CreateAsync(string scope)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, scope, context);
        }

        public async Task<Fixture> ReopenAsync(string requestedScope)
        {
            await Context.DisposeAsync();
            var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            return new Fixture(connection, requestedScope, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Accessor : IPersistenceAccessContextAccessor
    {
        public Accessor(string value) : this(PersistenceAccessContext.Scoped(new PersistenceScope(value))) { }

        public Accessor(PersistenceAccessContext current) => Current = current;

        public PersistenceAccessContext Current { get; }
    }

    private sealed class FaultingBookmarkStateDbContext(DbContextOptions<FaultingBookmarkStateDbContext> options) : BookmarkStateDbContext(options)
    {
        public bool FailNextSave { get; set; }

        protected override void ConfigureProvider(ModelBuilder modelBuilder) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new DbUpdateConcurrencyException("test concurrency failure");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
