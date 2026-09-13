using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
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
        await fixture.Store.SaveAsync(State("wf-null", "null-payload", "Event", "hash-null") with { Payload = null });

        var result = await fixture.Store.FindAsync("wf-1", "same");
        Assert.NotNull(result);
        Assert.Equal("/orders", result!.Payload!.Value.GetProperty("path").GetString());
        Assert.Equal("é", result.Payload.Value.GetProperty("value").GetString());
        Assert.Equal(state.Metadata, result.Metadata);
        Assert.Equal(state.CreatedAt, result.CreatedAt);
        Assert.Equal(state.ExpiresAt, result.ExpiresAt);
        Assert.Equal("hash-b", (await fixture.Store.FindAsync("wf-2", "same"))!.StimulusHash);
        Assert.Null((await fixture.Store.FindAsync("wf-null", "null-payload"))!.Payload);

        await using var otherScope = await fixture.ReopenAsync("tenant-b");
        Assert.Null(await otherScope.Store.FindAsync("wf-1", "same"));
    }

    [Fact]
    public async Task Distinct_malformed_utf16_scopes_remain_isolated()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-\uD800");
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "Event", "hash-a"));

        await using var otherScope = await fixture.ReopenAsync("tenant-\uD801");
        Assert.Null(await otherScope.Store.FindAsync("wf-1", "bm-1"));
    }

    [Fact]
    public async Task Pages_are_bounded_forward_and_stably_ordered_for_workflow_and_stimulus()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        foreach (var id in new[] { "c", "a", "b" })
            await fixture.Store.SaveAsync(State("wf-1", id, "Event", "same"));
        await fixture.Store.SaveAsync(State("wf-2", "a", "Event", "same") with { ExpiresAt = DateTimeOffset.UnixEpoch });

        var first = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-1", 2));
        Assert.Equal(new[] { "a", "b" }, first.Items.Select(x => x.BookmarkId));
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Store.ListPageAsync(new BookmarkStatePageQuery("wf-1", 2, first.NextContinuationToken));
        Assert.Equal(new[] { "c" }, second.Items.Select(x => x.BookmarkId));
        Assert.Null(second.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListPageAsync(
            new BookmarkStatePageQuery("wf-2", 2, first.NextContinuationToken)).AsTask());

        var index = (IBookmarkStimulusIndex)fixture.Store;
        var stimulus = await index.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "same", 2));
        Assert.Equal(new[] { "wf-1:a", "wf-1:b" }, stimulus.Items.Select(x => $"{x.WorkflowExecutionId}:{x.BookmarkId}"));
        Assert.NotNull(stimulus.NextContinuationToken);
        var stimulusSecond = await index.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("Event", "same", 2, stimulus.NextContinuationToken));
        Assert.Equal(new[] { "wf-1:c", "wf-2:a" }, stimulusSecond.Items.Select(x => $"{x.WorkflowExecutionId}:{x.BookmarkId}"));
        Assert.Equal(DateTimeOffset.UnixEpoch, stimulusSecond.Items[^1].ExpiresAt);
        var type = await index.ListByStimulusTypePageAsync(new BookmarkStimulusTypePageQuery("Event", 2));
        Assert.Equal(new[] { "wf-1:a", "wf-1:b" }, type.Items.Select(x => $"{x.WorkflowExecutionId}:{x.BookmarkId}"));
        await Assert.ThrowsAsync<ArgumentException>(() => index.ListByStimulusTypePageAsync(
            new BookmarkStimulusTypePageQuery("OtherEvent", 2, type.NextContinuationToken)).AsTask());
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
    public async Task Stimulus_continuations_are_bound_to_unambiguous_type_and_hash_pairs()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "a\0b", "c"));
        await fixture.Store.SaveAsync(State("wf-2", "bm-2", "a\0b", "c"));
        await fixture.Store.SaveAsync(State("wf-3", "bm-3", "a", "b\0c"));

        var first = await fixture.Store.ListByStimulusPageAsync(new BookmarkStimulusPageQuery("a\0b", "c", 1));
        Assert.NotNull(first.NextContinuationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery("a", "b\0c", 1, first.NextContinuationToken)).AsTask());
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
    public async Task Concurrent_creates_and_updates_have_one_deterministic_loser()
    {
        await using var database = await FileDatabase.CreateAsync();

        var createBarrier = new SaveBarrierInterceptor(2);
        await using (var left = database.Open(createBarrier))
        await using (var right = database.Open(createBarrier))
        {
            var outcomes = await Task.WhenAll(
                Capture(left.Store.SaveAsync(State("wf-create", "bm", "Event", "left"))),
                Capture(right.Store.SaveAsync(State("wf-create", "bm", "Event", "right"))));

            Assert.Single(outcomes, outcome => outcome is null);
            Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, outcome => outcome is not null));
        }

        await using (var seed = database.Open())
            await seed.Store.SaveAsync(State("wf-update", "bm", "Event", "seed"));

        var updateBarrier = new SaveBarrierInterceptor(2);
        await using (var left = database.Open(updateBarrier))
        await using (var right = database.Open(updateBarrier))
        {
            var outcomes = await Task.WhenAll(
                Capture(left.Store.SaveAsync(State("wf-update", "bm", "Event", "left"))),
                Capture(right.Store.SaveAsync(State("wf-update", "bm", "Event", "right"))));

            Assert.Single(outcomes, outcome => outcome is null);
            Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, outcome => outcome is not null));
        }
    }

    [Fact]
    public async Task Stale_delete_returns_false_and_does_not_remove_the_successor()
    {
        await using var database = await FileDatabase.CreateAsync();
        await using (var seed = database.Open())
            await seed.Store.SaveAsync(State("wf", "bm", "Event", "seed"));

        var pause = new PausingSaveInterceptor();
        await using var deleting = database.Open(pause);
        await using var updating = database.Open();
        var delete = deleting.Store.DeleteAsync("wf", "bm").AsTask();
        await pause.Entered;
        await updating.Store.SaveAsync(State("wf", "bm", "Event", "successor"));
        pause.Release();

        Assert.False(await delete);
        Assert.Equal("successor", (await updating.Store.FindAsync("wf", "bm"))!.StimulusHash);
    }

    [Fact]
    public async Task External_transaction_rollback_leaves_no_bookmark()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await fixture.Store.SaveAsync(State("wf-rollback", "bm", "Event", "hash"));
            await transaction.RollbackAsync();
        }

        fixture.Context.ChangeTracker.Clear();
        Assert.Null(await fixture.Store.FindAsync("wf-rollback", "bm"));
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
    public async Task Corrupt_encoded_scope_fails_closed_as_invalid_persisted_data()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-a");
        await fixture.Store.SaveAsync(State("wf-1", "bm-1", "Event", "hash"));
        var id = await fixture.Context.Bookmarks.Select(row => row.Id).SingleAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_runtime_bookmark_state SET ScopeKey = 'not-base64' WHERE Id = {0}", id);
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("wf-1", "bm-1").AsTask());
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

    [Fact]
    public async Task Shell_feature_registers_the_selected_provider_and_owned_store()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new RuntimeBookmarksEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<BookmarkStateSqliteDbContext>(scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>());
        Assert.IsType<EfBookmarkStateStore>(scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>());
    }

    [Fact]
    public void Named_connection_rejects_an_empty_configured_value_when_the_context_is_resolved()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:runtime"] = "   "
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddRuntimeBookmarksEntityFrameworkCore(new RuntimeBookmarksEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionName = "runtime"
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<BookmarkStateSqliteDbContext>());
        Assert.Contains("not found or was empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_is_idempotent_and_refuses_custom_ownership()
    {
        var options = new RuntimeBookmarksEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" };
        var repeated = new ServiceCollection();
        repeated.AddWorkflowRuntime();
        repeated.AddRuntimeBookmarksEntityFrameworkCore(options);
        repeated.AddRuntimeBookmarksEntityFrameworkCore(options);
        Assert.Single(repeated, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(repeated, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
        Assert.Throws<InvalidOperationException>(() => repeated.AddRuntimeBookmarksEntityFrameworkCore(
            new RuntimeBookmarksEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = "Data Source=other.db" }));

        var custom = new ServiceCollection();
        custom.AddSingleton<IBookmarkStateStore, CustomBookmarkStateStore>();
        Assert.Throws<InvalidOperationException>(() => custom.AddRuntimeBookmarksEntityFrameworkCore(options));

        var explicitInMemory = new ServiceCollection();
        explicitInMemory.AddSingleton<IBookmarkStateStore, InMemoryBookmarkStateStore>();
        Assert.Throws<InvalidOperationException>(() => explicitInMemory.AddRuntimeBookmarksEntityFrameworkCore(options));

        var customContext = new ServiceCollection();
        customContext.AddScoped<BookmarkStateSqliteDbContext>(_ => throw new NotSupportedException());
        Assert.Throws<InvalidOperationException>(() => customContext.AddRuntimeBookmarksEntityFrameworkCore(options));
        Assert.DoesNotContain(customContext, descriptor =>
            descriptor.ImplementationInstance is RuntimeBookmarksEntityFrameworkCoreOptions);

        var customIndex = new ServiceCollection();
        customIndex.AddWorkflowRuntime();
        customIndex.AddSingleton<IBookmarkStimulusIndex, CustomBookmarkStimulusIndex>();
        Assert.Throws<InvalidOperationException>(() => customIndex.AddRuntimeBookmarksEntityFrameworkCore(options));

        var defaultIndex = new ServiceCollection();
        defaultIndex.AddWorkflowRuntime();
        BookmarkStateStoreBackend.TryRegisterDefaultStimulusIndex(defaultIndex);
        defaultIndex.AddRuntimeBookmarksEntityFrameworkCore(options);
        Assert.Single(defaultIndex, descriptor => descriptor.ServiceType == typeof(IBookmarkStateStore));
        Assert.Single(defaultIndex, descriptor => descriptor.ServiceType == typeof(IBookmarkStimulusIndex));

    }

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

    private sealed class FileDatabase : IAsyncDisposable
    {
        private FileDatabase(string path) => Path = path;

        private string Path { get; }

        public static async Task<FileDatabase> CreateAsync()
        {
            var database = new FileDatabase(System.IO.Path.Join(System.IO.Path.GetTempPath(), $"elsa-runtime-bookmarks-{Guid.NewGuid():N}.db"));
            await using var fixture = database.Open();
            await fixture.Context.Database.EnsureCreatedAsync();
            return database;
        }

        public FileFixture Open(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>()
                .UseSqlite($"Data Source={Path}")
                .AddInterceptors(interceptor is null ? [] : [interceptor])
                .Options;
            var context = new BookmarkStateSqliteDbContext(options);
            return new FileFixture(context, new EfBookmarkStateStore(context, new Accessor("tenant-a")));
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(Path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FileFixture(BookmarkStateSqliteDbContext context, EfBookmarkStateStore store) : IAsyncDisposable
    {
        public BookmarkStateSqliteDbContext Context { get; } = context;
        public EfBookmarkStateStore Store { get; } = store;
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class SaveBarrierInterceptor(int participants) : SaveChangesInterceptor
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

    private sealed class PausingSaveInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;
        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class CustomBookmarkStateStore : IBookmarkStateStore
    {
        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CustomBookmarkStimulusIndex : IBookmarkStimulusIndex
    {
        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(BookmarkStimulusPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(BookmarkStimulusTypePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
