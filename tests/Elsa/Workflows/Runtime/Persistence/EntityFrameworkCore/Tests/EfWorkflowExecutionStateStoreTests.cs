using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowExecutionStateStoreTests
{
    [Fact]
    public void Registration_is_idempotent_and_rejects_foreign_ownership()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        var options = new RuntimeWorkflowExecutionEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = "01234567890123456789012345678901" };
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
        var count = services.Count;
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
        Assert.Equal(count, services.Count);

        var foreign = new ServiceCollection();
        foreign.AddSingleton<IWorkflowExecutionStateStore>(new InMemoryWorkflowExecutionStateStore());
        Assert.Throws<InvalidOperationException>(() => foreign.AddRuntimeWorkflowExecutionEntityFrameworkCore(options));
    }

    [Fact]
    public void Registration_can_follow_or_precede_runtime_defaults_without_duplicate_contracts()
    {
        var options = new RuntimeWorkflowExecutionEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = "01234567890123456789012345678901" };
        var afterDefaults = new ServiceCollection();
        afterDefaults.AddWorkflowRuntime();
        afterDefaults.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(afterDefaults)!.Name);
        Assert.Single(afterDefaults.Where(x => x.ServiceType == typeof(IWorkflowExecutionStateStore)));

        var beforeDefaults = new ServiceCollection();
        beforeDefaults.AddRuntimeWorkflowExecutionEntityFrameworkCore(options);
        beforeDefaults.AddWorkflowRuntime();
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(beforeDefaults)!.Name);
        Assert.Single(beforeDefaults.Where(x => x.ServiceType == typeof(IWorkflowExecutionStateStore)));
    }
    [Fact]
    public async Task Crud_restart_and_scope_isolation_round_trip_lossless_state()
    {
        await using var database = await Database.CreateAsync();
        var state = State("execution-1", "tenant-a", DateTimeOffset.UtcNow) with { CorrelationId = "correlation", Authority = new WorkflowExecutionAuthoritySnapshot("system", "root", new Dictionary<string, string> { ["region"] = "eu" }) };
        await using (var first = database.Open("tenant-a"))
        {
            Assert.Same(state, await first.Store.SaveAsync(state));
            var roundTrip = await first.Store.FindAsync(state.WorkflowExecutionId);
            Assert.Equal(state.WorkflowExecutionId, roundTrip!.WorkflowExecutionId);
            Assert.Equal(state.PinnedExecutable, roundTrip.PinnedExecutable);
            Assert.Equal(state.Authority!.SystemIdentity, roundTrip.Authority!.SystemIdentity);
            Assert.Equal(state.Authority.RootInitiator, roundTrip.Authority.RootInitiator);
            Assert.True(await first.Store.DeleteAsync(state.WorkflowExecutionId));
            Assert.False(await first.Store.DeleteAsync(state.WorkflowExecutionId));
            await first.Store.SaveAsync(state);
        }
        await using var restarted = database.Open("tenant-a");
        Assert.Equal(state.WorkflowExecutionId, (await restarted.Store.FindAsync(state.WorkflowExecutionId))!.WorkflowExecutionId);
        await using var other = database.Open("tenant-b");
        Assert.Null(await other.Store.FindAsync(state.WorkflowExecutionId));
    }

    [Fact]
    public async Task History_pages_are_bounded_stable_and_cursor_bound_to_filters()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var timestamp = DateTimeOffset.UtcNow;
        foreach (var id in new[] { "d", "b", "a", "c" }) await fixture.Store.SaveAsync(State(id, "tenant-a", timestamp));
        var first = await fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(2));
        await fixture.Store.SaveAsync(State("new", "tenant-a", timestamp.AddMinutes(1)));
        var second = await fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(2, Cursor: first.NextCursor));
        Assert.Equal(["a", "b"], first.Items.Select(x => x.WorkflowExecutionId));
        Assert.Equal(["c", "d"], second.Items.Select(x => x.WorkflowExecutionId));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(2, TenantId: "other", Cursor: first.NextCursor)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.QueryPageAsync(new WorkflowExecutionStatePageQuery(2, TenantId: "other")).AsTask());
    }

    [Fact]
    public async Task Pinned_artifacts_are_projected_without_returning_state_documents()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var artifacts = new[] { "😀😀", "😀a", "😀", "\uE000", "😀" };
        for (var index = 0; index < artifacts.Length; index++)
            await fixture.Store.SaveAsync(State($"execution-{index}", "tenant-a", DateTimeOffset.UtcNow) with { PinnedExecutable = new(artifacts[index], "definition", "version", "1", "hash") });
        Assert.Equal(["😀", "😀a", "😀😀", "\uE000"], await fixture.Store.ListPinnedExecutableArtifactIdsAsync());
    }

    [Fact]
    public async Task Alteration_capture_is_immutable_and_cursor_bound()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var authority = new WorkflowExecutionAuthoritySnapshot("system", "root", new Dictionary<string, string> { ["region"] = "eu" });
        await fixture.Store.SaveAsync(State("a", "tenant-a", DateTimeOffset.UtcNow) with { Authority = authority });
        await fixture.Store.SaveAsync(State("b", "tenant-a", DateTimeOffset.UtcNow) with { Authority = authority });
        var query = new WorkflowExecutionAlterationCaptureQuery("tenant-a", "system", "root", new Dictionary<string, string> { ["region"] = "eu" }, new WorkflowAlterationQuerySelector(matchAllAuthorized: true), 1);
        var first = await fixture.Store.QueryAlterationCapturePageAsync(query);
        await fixture.Store.SaveAsync(State("a", "tenant-a", DateTimeOffset.UtcNow.AddMinutes(2)) with { Authority = authority });
        var second = await fixture.Store.QueryAlterationCapturePageAsync(new WorkflowExecutionAlterationCaptureQuery("tenant-a", "system", "root", new Dictionary<string, string> { ["region"] = "eu" }, new WorkflowAlterationQuerySelector(matchAllAuthorized: true), 1, first.NextCursor));
        Assert.Equal("a", first.Items[0].WorkflowExecutionId);
        Assert.Equal("b", second.Items[0].WorkflowExecutionId);
    }

    [Fact]
    public async Task Corrupt_projection_is_rejected_fail_closed()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(State("execution", "tenant-a", DateTimeOffset.UtcNow));
        var row = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        row.Status = (int)WorkflowExecutionStatus.Faulted;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("execution").AsTask());
    }

    [Fact]
    public async Task Save_rejects_state_from_another_tenant_scope()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.SaveAsync(State("execution", "tenant-b", DateTimeOffset.UtcNow)).AsTask());
    }

    [Fact]
    public async Task Invalid_revision_and_json_fail_closed_without_poisoning_the_tracker()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = State("execution", "tenant-a", DateTimeOffset.UtcNow);
        await fixture.Store.SaveAsync(state);
        var row = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        row.Revision = 0;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.SaveAsync(state with { Status = WorkflowExecutionStatus.Running }).AsTask());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        row = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        row.Revision = 1;
        await fixture.Context.SaveChangesAsync();
        row.ContentJson = "not-json";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync(state.WorkflowExecutionId).AsTask());
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Concurrent_create_race_leaves_one_authoritative_row_and_recoverable_contexts()
    {
        await using var database = await Database.CreateAsync();
        var state = State("race", "tenant-a", DateTimeOffset.UtcNow);
        await using var left = database.Open("tenant-a");
        await using var right = database.Open("tenant-a");
        var outcomes = await Task.WhenAll(
            Capture(left.Store.SaveAsync(state)),
            Capture(right.Store.SaveAsync(state)));
        Assert.All(outcomes, outcome => Assert.True(outcome is null or InvalidOperationException));
        await using var verification = database.Open("tenant-a");
        Assert.NotNull(await verification.Store.FindAsync(state.WorkflowExecutionId));
        Assert.Single(await verification.Context.WorkflowExecutionStates.ToArrayAsync());
        left.Context.ChangeTracker.Clear();
        right.Context.ChangeTracker.Clear();
        Assert.Empty(left.Context.ChangeTracker.Entries());
        Assert.Empty(right.Context.ChangeTracker.Entries());
    }

    private static async Task<Exception?> Capture(ValueTask<WorkflowExecutionState> operation)
    {
        try { await operation; return null; }
        catch (Exception exception) when (exception is InvalidOperationException) { return exception; }
    }

    private static WorkflowExecutionState State(string id, string tenant, DateTimeOffset timestamp) => new(id, new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Completed, null, timestamp.AddMinutes(-1), timestamp.AddMinutes(-1), timestamp, timestamp, null, null, tenant, new Dictionary<string, string>());

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Database(SqliteConnection connection) => _connection = connection;
        public static async Task<Database> CreateAsync() { var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options); await context.Database.EnsureCreatedAsync(); return new Database(connection); }
        public Fixture Open(string scope) => new(_connection, scope);
        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public readonly BookmarkStateSqliteDbContext Context;
        public readonly EfWorkflowExecutionStateStore Store;
        public Fixture(SqliteConnection connection, string scope) { _connection = connection; Context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options); Store = new EfWorkflowExecutionStateStore(Context, new Accessor(scope), new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "01234567890123456789012345678901" }))); }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); }
    }
    private sealed class Accessor(string value) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(value)); }
}
