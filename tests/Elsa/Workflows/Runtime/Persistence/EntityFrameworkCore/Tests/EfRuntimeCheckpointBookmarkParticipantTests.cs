using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointBookmarkParticipantTests
{
    private static readonly DateTimeOffset CreatedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Upsert_is_staged_with_sibling_and_marker_in_the_callers_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(Bookmark("bookmark-a", "workflow-a", "before"));

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, Bookmark("bookmark-a", "workflow-a", "after")), "tenant-a");
            fixture.Context.RuntimeCheckpointCommits.Add(Marker("tenant-a", "commit-upsert", "workflow-a"));
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        var bookmark = await verification.Store.FindAsync("workflow-a", "bookmark-a");
        Assert.Equal("after", bookmark!.Payload!.Value.GetString());
        Assert.Equal("stimulus-after", bookmark.StimulusHash);
        Assert.Single(await verification.Context.SchedulerStates.ToArrayAsync());
        Assert.Single(await verification.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Rollback_discards_staged_upsert_and_sibling_marker_without_independent_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var original = Bookmark("bookmark-a", "workflow-a", "before");
        await fixture.Store.SaveAsync(original);
        var originalRevision = await fixture.Context.Bookmarks.Select(row => row.Revision).SingleAsync();

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, Bookmark("bookmark-a", "workflow-a", "after")), "tenant-a");
            fixture.Context.RuntimeCheckpointCommits.Add(Marker("tenant-a", "commit-rollback", "workflow-a"));
            await fixture.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verification = database.Open("tenant-a");
        var bookmark = await verification.Store.FindAsync("workflow-a", "bookmark-a");
        Assert.Equal("before", bookmark!.Payload!.Value.GetString());
        Assert.Equal(original.StimulusHash, bookmark.StimulusHash);
        Assert.Equal(originalRevision, await verification.Context.Bookmarks.Select(row => row.Revision).SingleAsync());
        Assert.Empty(await verification.Context.SchedulerStates.ToArrayAsync());
        Assert.Empty(await verification.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Delete_is_staged_and_missing_delete_is_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var bookmark = Bookmark("bookmark-a", "workflow-a", "before");
        await fixture.Store.SaveAsync(bookmark);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, bookmark), "tenant-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, bookmark), "tenant-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        Assert.Null(await verification.Store.FindAsync("workflow-a", "bookmark-a"));
    }

    [Fact]
    public async Task Staging_requires_a_caller_owned_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var bookmark = Bookmark("bookmark-a", "workflow-a", "before");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, bookmark), "tenant-a"));
        Assert.Empty(await fixture.Context.Bookmarks.ToArrayAsync());
    }

    [Fact]
    public async Task Staged_upsert_uses_revision_concurrency_and_preserves_the_winner()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var seed = database.Open("tenant-a");
        await seed.Store.SaveAsync(Bookmark("bookmark-a", "workflow-a", "before"));

        await using var stale = database.Open("tenant-a");
        _ = await stale.Context.Bookmarks.SingleAsync();
        await using (var winner = database.Open("tenant-a"))
            await winner.Store.SaveAsync(Bookmark("bookmark-a", "workflow-a", "winner"));

        await using var transaction = await stale.Context.Database.BeginTransactionAsync();
        await StageAsync(stale.Context, Change(RuntimeStateChangeOperation.Upsert, Bookmark("bookmark-a", "workflow-a", "stale")), "tenant-a");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
        await transaction.RollbackAsync();

        await using var verification = database.Open("tenant-a");
        Assert.Equal("winner", (await verification.Store.FindAsync("workflow-a", "bookmark-a"))!.Payload!.Value.GetString());
    }

    [Fact]
    public async Task Staging_is_scope_isolated_and_retry_after_rollback_replays_one_bookmark()
    {
        await using var database = await TestDatabase.CreateAsync();
        var bookmark = Bookmark("bookmark-a", "workflow-a", "before");
        await using (var tenantA = database.Open("tenant-a"))
            await tenantA.Store.SaveAsync(bookmark);
        await using (var tenantB = database.Open("tenant-b"))
            await tenantB.Store.SaveAsync(bookmark);

        await using (var tenantA = database.Open("tenant-a"))
        {
            await using var transaction = await tenantA.Context.Database.BeginTransactionAsync();
            await StageAsync(tenantA.Context, Change(RuntimeStateChangeOperation.Upsert, Bookmark("bookmark-a", "workflow-a", "retry")), "tenant-a");
            await tenantA.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var retry = database.Open("tenant-a"))
        {
            await using var transaction = await retry.Context.Database.BeginTransactionAsync();
            await StageAsync(retry.Context, Change(RuntimeStateChangeOperation.Upsert, Bookmark("bookmark-a", "workflow-a", "retry")), "tenant-a");
            await retry.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verificationA = database.Open("tenant-a");
        await using var verificationB = database.Open("tenant-b");
        Assert.Equal("retry", (await verificationA.Store.FindAsync("workflow-a", "bookmark-a"))!.Payload!.Value.GetString());
        Assert.Equal("before", (await verificationB.Store.FindAsync("workflow-a", "bookmark-a"))!.Payload!.Value.GetString());
    }

    private static RuntimeStateChange<BookmarkState> Change(RuntimeStateChangeOperation operation, BookmarkState state) =>
        new(state.BookmarkId, operation, state, new Dictionary<string, string>());

    private static BookmarkState Bookmark(string bookmarkId, string workflowExecutionId, string payload) =>
        new(
            bookmarkId,
            workflowExecutionId,
            "activity-a",
            "node-a",
            "resume-a",
            "stimulus",
            $"stimulus-{payload}",
            JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement,
            new Dictionary<string, string> { ["kind"] = "test" },
            CreatedAt,
            CreatedAt.AddHours(1));

    private static async Task StageAsync(BookmarkStateDbContext context, RuntimeStateChange<BookmarkState> change, string scope)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointParticipantStaging")!;
        var method = type.GetMethod("StageBookmarksAsync", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            var result = (ValueTask)method.Invoke(null, [context, new[] { change }, scope, CancellationToken.None])!;
            await result.AsTask();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static SchedulerStateEntity SchedulerRow(string scope, string workflowExecutionId) => new()
    {
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{workflowExecutionId.Length}:{workflowExecutionId}"),
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
        WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        Collection = "schedulerState",
        ContentJson = "{}",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static RuntimeCheckpointCommitEntity Marker(string scope, string commitId, string workflowExecutionId) => new()
    {
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{commitId.Length}:{commitId}"),
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        CommitId = EfRelationalIdentity.Encode(commitId),
        CommitIdHash = EfRelationalIdentity.Hash(commitId),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
        WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        OccurredAtUtcTicks = CreatedAt.UtcTicks,
        Fingerprint = new string('a', 64),
        ContentJson = "{}",
        PendingPostCommitWorkIdsJson = "[]",
        ConsumedSchedulerWorkItemIdsJson = "[]",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r19-bookmark-participant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public TestFixture Open(string scope) => new(connectionString, scope);
        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public BookmarkStateSqliteDbContext Context { get; }
        public EfBookmarkStateStore Store { get; }

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            Store = new EfBookmarkStateStore(Context, new FixedAccessor(scope));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
