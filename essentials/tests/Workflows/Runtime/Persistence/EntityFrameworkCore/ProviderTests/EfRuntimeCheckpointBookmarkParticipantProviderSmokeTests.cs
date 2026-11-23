using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeCheckpointBookmarkParticipantPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_checkpoint_bookmark_participant_smoke() =>
        RuntimeCheckpointBookmarkParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
            RuntimePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointBookmarkParticipantSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_checkpoint_bookmark_participant_smoke() =>
        RuntimeCheckpointBookmarkParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
            RuntimeSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointBookmarkParticipantMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_checkpoint_bookmark_participant_smoke() =>
        RuntimeCheckpointBookmarkParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeCheckpointBookmarkParticipantProviderSmoke
{
    private static readonly DateTimeOffset CreatedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, RuntimeDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r19-bookmarks-{Guid.NewGuid():N}";
        var original = Bookmark("bookmark-a", "workflow-a", "before");

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);
            await store.SaveAsync(original);

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.SchedulerStates.Add(SchedulerRow(scope, "workflow-a"));
                await StageAsync(context, Change(RuntimeStateChangeOperation.Upsert, Bookmark("bookmark-a", "workflow-a", "committed")), scope);
                context.RuntimeCheckpointCommits.Add(Marker(scope, "commit-bookmark", "workflow-a"));
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            Assert.Equal("committed", (await store.FindAsync("workflow-a", "bookmark-a"))!.Payload!.Value.GetString());

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await StageAsync(context, Change(RuntimeStateChangeOperation.Delete, Bookmark("bookmark-a", "workflow-a", "committed")), scope);
                await context.SaveChangesAsync();
                await transaction.RollbackAsync();
            }
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            Assert.Equal("committed", (await Store(verification, scope).FindAsync("workflow-a", "bookmark-a"))!.Payload!.Value.GetString());
            Assert.Single(await verification.SchedulerStates.Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).ToArrayAsync());
            Assert.Single(await verification.RuntimeCheckpointCommits.Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).ToArrayAsync());
        }

        var otherScope = $"{scope}-other";
        await using (var other = createContext(fixture.ConnectionString))
            await Store(other, otherScope).SaveAsync(original);

        await using (var tenantA = createContext(fixture.ConnectionString))
        {
            await using var transaction = await tenantA.Database.BeginTransactionAsync();
            await StageAsync(tenantA, Change(RuntimeStateChangeOperation.Delete, Bookmark("bookmark-a", "workflow-a", "committed")), scope);
            await tenantA.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var isolation = createContext(fixture.ConnectionString))
        {
            Assert.Null(await Store(isolation, scope).FindAsync("workflow-a", "bookmark-a"));
            Assert.NotNull(await Store(isolation, otherScope).FindAsync("workflow-a", "bookmark-a"));
        }

        var conflictScope = $"{scope}-conflict";
        await using (var seed = createContext(fixture.ConnectionString))
            await Store(seed, conflictScope).SaveAsync(Bookmark("bookmark-c", "workflow-c", "before"));
        await using var stale = createContext(fixture.ConnectionString);
        _ = await stale.Bookmarks.SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(conflictScope));
        await using (var winner = createContext(fixture.ConnectionString))
            await Store(winner, conflictScope).SaveAsync(Bookmark("bookmark-c", "workflow-c", "winner"));
        await using var conflictTransaction = await stale.Database.BeginTransactionAsync();
        await StageAsync(stale, Change(RuntimeStateChangeOperation.Upsert, Bookmark("bookmark-c", "workflow-c", "stale")), conflictScope);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await conflictTransaction.RollbackAsync();
        await using var conflictVerification = createContext(fixture.ConnectionString);
        Assert.Equal("winner", (await Store(conflictVerification, conflictScope).FindAsync("workflow-c", "bookmark-c"))!.Payload!.Value.GetString());
    }

    private static EfBookmarkStateStore Store(RuntimeDbContext context, string scope) =>
        new(context, new FixedAccessor(scope));

    private static RuntimeStateChange<BookmarkState> Change(RuntimeStateChangeOperation operation, BookmarkState state) =>
        new(state.BookmarkId, operation, state, new Dictionary<string, string>());

    private static BookmarkState Bookmark(string bookmarkId, string workflowExecutionId, string payload) =>
        new(bookmarkId, workflowExecutionId, "activity-a", "node-a", "resume-a", "stimulus", $"stimulus-{payload}",
            JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement,
            new Dictionary<string, string> { ["kind"] = "test" }, CreatedAt, CreatedAt.AddHours(1));

    private static async Task StageAsync(RuntimeDbContext context, RuntimeStateChange<BookmarkState> change, string scope)
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
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{workflowExecutionId.Length}:{workflowExecutionId}"), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId), WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId), WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        Collection = "schedulerState", ContentJson = "{}", SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = 1
    };

    private static RuntimeCheckpointCommitEntity Marker(string scope, string commitId, string workflowExecutionId) => new()
    {
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{commitId.Length}:{commitId}"), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        CommitId = EfRelationalIdentity.Encode(commitId), CommitIdHash = EfRelationalIdentity.Hash(commitId),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId), WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId), WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        OccurredAtUtcTicks = CreatedAt.UtcTicks, Fingerprint = new string('a', 64), ContentJson = "{}", PendingPostCommitWorkIdsJson = "[]", ConsumedSchedulerWorkItemIdsJson = "[]",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = 1
    };

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
