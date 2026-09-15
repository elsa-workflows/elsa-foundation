using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeWorkflowTestScopeCleanupPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_test_scope_cleanup_model_and_transaction() => RuntimeWorkflowTestScopeCleanupProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
        BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeWorkflowTestScopeCleanupSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_test_scope_cleanup_model_and_transaction() => RuntimeWorkflowTestScopeCleanupProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
        BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeWorkflowTestScopeCleanupMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_test_scope_cleanup_model_and_transaction() => RuntimeWorkflowTestScopeCleanupProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
        BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeWorkflowTestScopeCleanupProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var tenant = $"cleanup-native-{Guid.NewGuid():N}";
        var scope = new WorkflowTestScope(
            "native-cleanup-scope",
            Now.AddHours(1),
            tenant,
            new WorkflowExecutionPartition("native-cleanup-partition"));
        var codec = new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));

        await using var context = createContext(fixture.ConnectionString);
        Assert.Equal(expectedProvider, context.Database.ProviderName);
        await context.Database.EnsureCreatedAsync();
        var access = new FixedAccessor(tenant);
        var scopes = new EfWorkflowTestScopeStore(context, access, codec);
        var dispatches = new EfWorkflowDispatchStore(context, access);
        var cleanup = new EfWorkflowTestScopeCleanupStore(context, access, codec);
        await scopes.CreateAsync(scope, Now);
        var pending = Dispatch("pending", scope);
        var started = Dispatch("started", scope, Now.AddSeconds(1));
        await dispatches.SaveAsync(pending);
        await dispatches.SaveAsync(started);
        started = started.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(2));
        await dispatches.SaveAsync(started);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, Now.AddMinutes(1)));

        var result = await cleanup.CleanupAsync(
            scope,
            Now.AddMinutes(1),
            100,
            new Dictionary<string, RuntimePostCommitIntent> { [started.DispatchId] = CancellationIntent(started) });

        Assert.Equal(2, result.Inspected);
        Assert.Equal(1, result.CancelledBeforeAdmission);
        Assert.Equal(1, result.CancellationQueued);
        Assert.Equal(1, result.RemainingLive);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, (await dispatches.FindAsync(pending.DispatchId))!.Status);
        Assert.True(WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(
            (await dispatches.FindAsync(started.DispatchId))!));
    }

    private static WorkflowDispatchRecord Dispatch(string id, WorkflowTestScope scope, DateTimeOffset? createdAt = null)
    {
        var parent = $"parent-{id}";
        var activity = $"activity-{id}";
        var identity = new WorkflowDispatchIdentity(parent, activity);
        var timestamp = createdAt ?? Now;
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity($"artifact-{id}", "definition-child", "version-child", "1", $"hash-{id}"),
            new WorkflowExecutableSourceProvenance($"source-{id}", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            scope.TenantId,
            scope.Partition,
            WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "native-initiator"),
            [],
            timestamp,
            timestamp,
            new Dictionary<string, string>(),
            testScope: scope);
    }

    private static RuntimePostCommitIntent CancellationIntent(WorkflowDispatchRecord started)
    {
        var identity = new WorkflowDispatchIdentity(started.ParentWorkflowExecutionId, started.ParentActivityExecutionId);
        return new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            started.ParentWorkflowExecutionId,
            "Elsa.Activities.DispatchWorkflow.CancelChild",
            Now.AddMinutes(1),
            started.ParentActivityExecutionId,
            identity.ChildCancelIdempotencyKey,
            payload: null,
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = started.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = started.ChildWorkflowExecutionId
            });
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
