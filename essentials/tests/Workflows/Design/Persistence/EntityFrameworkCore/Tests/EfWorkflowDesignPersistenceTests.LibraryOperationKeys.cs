using Elsa.Locking.Core;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

// Spec 1193 FR-021 and SC-008: a keyed tag or view create resolves, by its client operation key, to exactly one resource.
public sealed partial class EfWorkflowDesignPersistenceTests
{
    // Records holding collections compare those by reference; the wire shape is the resource's identity and state.
    private static void AssertSameResource<T>(T expected, T? actual) =>
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(actual));

    [Fact]
    public async Task Library_keyed_tag_create_returns_the_keyed_tag_refuses_other_input_and_scopes_the_key_by_tenant()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-tag-keys");
        string tagId;
        await using (var host = LibraryHost(database.ConnectionString))
        {
            using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            // The migration chain, not the model, so the new columns and unique index are what the migration creates.
            await db.Database.MigrateAsync();
            var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            Assert.Null(await library.FindTagByOperationKeyAsync("create-critical"));
            var created = await library.CreateTagAsync(new("Critical"), "alice", "create-critical"); tagId = created.Id;
            // The response was lost: the same key and input return the one tag and write nothing.
            var auditCount = await db.LibraryAudit.CountAsync();
            var repeated = await library.CreateTagAsync(new(" Critical "), "alice", "create-critical");
            AssertSameResource(created, repeated);
            Assert.Equal(auditCount, await db.LibraryAudit.CountAsync());
            AssertSameResource(created, await library.FindTagByOperationKeyAsync("create-critical"));
            // The same key with different input is a conflict; nothing is created.
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateTagAsync(new("Urgent"), "alice", "create-critical"));
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateTagAsync(new("Critical", Description: "Other"), "alice", "create-critical"));
            // A different key cannot create a second tag with the same name; unkeyed and keyed creates coexist.
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.CreateTagAsync(new("Critical"), "alice", "create-critical-again"));
            Assert.Null(await library.FindTagByOperationKeyAsync("create-critical-again"));
            await library.CreateTagAsync(new("Unkeyed one"), "alice"); await library.CreateTagAsync(new("Unkeyed two"), "alice");
            var controlled = await library.CreateTagAsync(new("Region", Mode: "controlled", Values: [new("", "EU"), new("", "US")]), "alice", "create-region");
            AssertSameResource(controlled, await library.CreateTagAsync(new("Region", Mode: "controlled", Values: [new("", "EU"), new("", "US")]), "alice", "create-region"));
            await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() =>
                library.CreateTagAsync(new("Region", Mode: "controlled", Values: [new("", "EU")]), "alice", "create-region"));
            Assert.Equal(4, (await library.ListTagsAsync()).Items.Count);
            // Only the key's lookup hash is stored, never the client key itself.
            Assert.DoesNotContain(await db.Tags.Select(row => row.OperationKey).ToArrayAsync(), key => key == "create-critical");
            await Assert.ThrowsAsync<ArgumentException>(() => library.CreateTagAsync(new("Bad key"), "alice", new string('k', 257)));
            await Assert.ThrowsAsync<ArgumentException>(() => library.CreateTagAsync(new("Bad key"), "alice", "line\nbreak"));
        }
        await using (var other = LibraryHost(database.ConnectionString, tenant: "tenant-b"))
        {
            using var scope = other.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            // Another tenant's key space is its own: the same key reads nothing and creates that tenant's tag.
            Assert.Null(await library.FindTagByOperationKeyAsync("create-critical"));
            var foreign = await library.CreateTagAsync(new("Critical"), "bob", "create-critical");
            Assert.NotEqual(tagId, foreign.Id);
        }
        await using (var restart = LibraryHost(database.ConnectionString))
        {
            using var scope = restart.CreateScope(); var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            Assert.Equal(tagId, (await library.FindTagByOperationKeyAsync("create-critical"))!.Id);
        }
    }

    [Fact]
    public async Task Library_concurrent_keyed_tag_creates_with_one_key_resolve_to_exactly_one_tag()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-tag-key-race");
        await using var host = LibraryHost(database.ConnectionString);
        using (var setup = host.CreateScope()) await setup.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Database.MigrateAsync();
        async Task<WorkflowTag> CreateAsync()
        {
            using var scope = host.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>().CreateTagAsync(new("Raced"), "alice", "race-key");
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateAsync()));
        Assert.Single(results.Select(tag => tag.Id).Distinct());
        using var read = host.CreateScope();
        Assert.Equal(1, await read.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Tags.CountAsync());
    }

    [Fact]
    public async Task Library_keyed_view_create_returns_the_keyed_view_refuses_other_input_and_scopes_the_key_by_owner()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-view-keys"); await using var host = LibraryHost(database.ConnectionString);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.MigrateAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        WorkflowLibraryViewInput Input(string name = "Finance", bool shared = false) => new(name, new(SearchTerm: "pay", Sort: "modified"), shared, ["name", "tags"]);
        Assert.Null(await library.FindViewByOperationKeyAsync("save-finance", "alice"));
        var created = await library.SaveViewAsync(null, Input(), "alice", false, "save-finance");
        var auditCount = await db.LibraryAudit.CountAsync();
        AssertSameResource(created, await library.SaveViewAsync(null, Input(), "alice", false, "save-finance"));
        Assert.Equal(auditCount, await db.LibraryAudit.CountAsync());
        AssertSameResource(created, await library.FindViewByOperationKeyAsync("save-finance", "alice"));
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.SaveViewAsync(null, Input("Other"), "alice", false, "save-finance"));
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.SaveViewAsync(null, Input(shared: true), "alice", true, "save-finance"));
        // The key is scoped by owner as well as tenant: another subject neither reads alice's key nor collides with it.
        Assert.Null(await library.FindViewByOperationKeyAsync("save-finance", "bob"));
        var bobs = await library.SaveViewAsync(null, Input(), "bob", false, "save-finance");
        Assert.NotEqual(created.Id, bobs.Id);
        // Sharing still requires the share permission; an update stays bound by its revision, never by a key.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => library.SaveViewAsync(null, Input("Team", shared: true), "alice", false, "save-team"));
        Assert.Null(await library.FindViewByOperationKeyAsync("save-team", "alice"));
        await Assert.ThrowsAsync<ArgumentException>(() => library.SaveViewAsync(created.Id, Input() with { Revision = created.Revision }, "alice", false, "update-key"));
        Assert.Equal(2, (await library.ListViewsAsync("alice")).Count + (await library.ListViewsAsync("bob")).Count);
        var updated = await library.SaveViewAsync(created.Id, Input("Renamed") with { Revision = created.Revision }, "alice", false);
        // The keyed create remains the view that key made, at its current revision.
        Assert.Equal(updated.Revision, (await library.FindViewByOperationKeyAsync("save-finance", "alice"))!.Revision);
        Assert.Equal(updated.Id, (await library.SaveViewAsync(null, Input(), "alice", false, "save-finance")).Id);
    }

    [Fact]
    public async Task Library_the_writers_operation_key_stays_server_generated_and_a_repeated_keyed_create_touches_no_fence()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-key-ledger"); await using var host = LibraryHost(database.ConnectionString);
        using var scope = host.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.MigrateAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var tag = await library.CreateTagAsync(new("Ledger"), "alice", "client-ledger-key");
        var view = await library.SaveViewAsync(null, new("Ledger view", new()), "alice", false, "client-ledger-view-key");
        // FR-021, last clause: the designOperation ledger and the audit carry the writer's own fresh key, never the client's.
        var ledger = await db.Operations.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, ledger.Length);
        Assert.All(ledger, row => Assert.DoesNotContain("client-ledger", row.OperationKey));
        Assert.Contains(ledger, row => row.OperationKey.StartsWith("create-tag-", StringComparison.Ordinal));
        Assert.Contains(ledger, row => row.OperationKey.StartsWith("save-view-", StringComparison.Ordinal));
        Assert.All(await db.LibraryAudit.AsNoTracking().ToArrayAsync(), row => Assert.DoesNotContain("client-ledger", row.OperationKey));
        // A repeat resolves by rereading the key: no ledger row, audit row or hierarchy-fence change.
        var fence = (await db.LibraryFences.AsNoTracking().SingleAsync()).Revision;
        Assert.Equal(tag.Id, (await library.CreateTagAsync(new("Ledger"), "alice", "client-ledger-key")).Id);
        Assert.Equal(view.Id, (await library.SaveViewAsync(null, new("Ledger view", new()), "alice", false, "client-ledger-view-key")).Id);
        Assert.Equal(fence, (await db.LibraryFences.AsNoTracking().SingleAsync()).Revision);
        Assert.Equal(2, await db.Operations.CountAsync());
    }

    [Fact]
    public async Task Library_concurrent_keyed_view_creates_with_one_key_resolve_to_exactly_one_view()
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-view-key-race");
        await using var host = LibraryHost(database.ConnectionString);
        using (var setup = host.CreateScope()) await setup.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Database.MigrateAsync();
        async Task<WorkflowLibraryView> SaveAsync()
        {
            using var scope = host.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>().SaveViewAsync(null, new("Raced view", new(SearchTerm: "pay")), "alice", false, "race-view-key");
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SaveAsync()));
        Assert.Single(results.Select(view => view.Id).Distinct());
        using var read = host.CreateScope();
        Assert.Equal(1, await read.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().LibraryViews.CountAsync());
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("view")]
    public async Task Library_a_key_committed_between_the_reread_and_the_write_rolls_the_loser_back_without_fence_ledger_or_audit(string kind)
    {
        await using var database = new TemporarySqliteDatabase("workflow-library-key-raced-" + kind);
        // The hierarchy lease is taken after the keyed create's own reread and before its write, so a hook there commits the
        // racing create exactly in the window the in-writer key check guards.
        var hook = new HookLockProvider();
        // Observes every command: the in-writer key check rolls the loser back before it inserts, so no insert of the loser
        // runs and nothing fails; the catch fallback (a unique-index violation) would show a failed insert instead.
        var sql = new KeyedInsertRecorder(kind == "tag" ? "elsa_workflow_tags" : "elsa_workflow_library_views");
        await using var host = LibraryHost(database.ConnectionString, locking: hook, failure: sql);
        using (var setup = host.CreateScope()) await setup.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Database.MigrateAsync();
        Task<string> CreateAsync(IServiceProvider services) => kind == "tag"
            ? services.GetRequiredService<IWorkflowLibraryStore>().CreateTagAsync(new("Raced"), "alice", "raced-key").ContinueWith(task => task.Result.Id)
            : services.GetRequiredService<IWorkflowLibraryStore>().SaveViewAsync(null, new("Raced", new()), "alice", false, "raced-key").ContinueWith(task => task.Result.Id);
        string? winner = null; (string Fence, int Ledger, int Audit) committed = default;
        async Task<(string, int, int)> CountsAsync()
        {
            using var read = host.CreateScope(); var db = read.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            return ((await db.LibraryFences.AsNoTracking().SingleAsync()).Revision, await db.Operations.CountAsync(), await db.LibraryAudit.CountAsync());
        }
        hook.Once = async () =>
        {
            using var racer = host.CreateScope();
            winner = await CreateAsync(racer.ServiceProvider);
            committed = await CountsAsync();
        };
        using var scope = host.CreateScope();
        var insertsBefore = 0;
        var winnerInserts = 0;
        hook.After = () => { winnerInserts = sql.Inserts - insertsBefore; };
        insertsBefore = sql.Inserts;
        // The branch itself, observed: the loser's write found the key committed and rolled back (never a name conflict,
        // never a unique-index failure).
        var causes = new List<string>();
        var keyHash = Stores.EfWorkflowLibraryStore.ClientKeyHashOf("raced-key");
        void Record(string hash, string cause) { if (hash == keyHash) lock (causes) causes.Add(cause); }
        Stores.EfWorkflowLibraryStore.KeyedCreateResolved += Record;
        string loser;
        try { loser = await CreateAsync(scope.ServiceProvider); }
        finally { Stores.EfWorkflowLibraryStore.KeyedCreateResolved -= Record; }
        Assert.Equal(new[] { "KeyedCreateRaced" }, causes);
        Assert.True(hook.Fired);
        Assert.Equal(winner, loser);
        Assert.Equal(1, winnerInserts);
        Assert.Equal(1, sql.Inserts - insertsBefore);
        Assert.Empty(sql.Failures);
        // The loser wrote nothing: the same fence revision, ledger rows and audit rows the winner left.
        Assert.Equal(committed, await CountsAsync());
        using var final = host.CreateScope(); var rows = final.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
        Assert.Equal(1, kind == "tag" ? await rows.Tags.CountAsync() : await rows.LibraryViews.CountAsync());
    }

    private sealed class HookLockProvider : IDistributedLockProvider
    {
        public Func<Task>? Once { get; set; }
        public Action? After { get; set; }
        public bool Fired { get; private set; }
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public async ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (Once is { } hook) { Once = null; Fired = true; await hook(); After?.Invoke(); }
            return new Handle();
        }
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        private sealed class Handle : IDistributedSynchronizationHandle { public CancellationToken HandleLostToken => CancellationToken.None; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    private sealed class KeyedInsertRecorder(string table) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int inserts;
        public int Inserts => Volatile.Read(ref inserts);
        public List<string> Failures { get; } = [];
        private void Observe(System.Data.Common.DbCommand command)
        {
            if (command.CommandText.Contains("INSERT INTO \"" + table + "\"", StringComparison.Ordinal)) Interlocked.Increment(ref inserts);
        }
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override Task CommandFailedAsync(System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        { lock (Failures) Failures.Add(eventData.Exception.GetType().Name); return Task.CompletedTask; }
    }
}
