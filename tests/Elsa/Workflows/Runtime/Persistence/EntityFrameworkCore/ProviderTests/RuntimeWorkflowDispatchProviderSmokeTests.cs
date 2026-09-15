using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeWorkflowDispatchPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_workflow_dispatch_model_crud_query_and_cas() => RuntimeWorkflowDispatchProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
        BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeWorkflowDispatchSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_workflow_dispatch_model_crud_query_and_cas() => RuntimeWorkflowDispatchProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
        BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeWorkflowDispatchMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_workflow_dispatch_model_crud_query_and_cas() => RuntimeWorkflowDispatchProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
        BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeWorkflowDispatchProviderSmoke
{
    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, BookmarkStateDbContext> createContext, string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"dispatch-native-{Guid.NewGuid():N}";
        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var access = new FixedAccessor(scope);
            var store = new EfWorkflowDispatchStore(context, access);
            var first = Pending("parent-a", "activity-a", scope, DateTimeOffset.UtcNow);
            var second = Pending("parent-a", "activity-b", scope, DateTimeOffset.UtcNow.AddTicks(1));
            await store.SaveAsync(first);
            await store.SaveAsync(first);
            await store.SaveAsync(second);

            var page = await store.QueryAsync(new WorkflowDispatchQuery(parentWorkflowExecutionId: "parent-a", take: 1));
            Assert.Single(page);
            var rest = await store.QueryAsync(new WorkflowDispatchQuery(
                parentWorkflowExecutionId: "parent-a",
                take: 10,
                afterCreatedAt: page.Single().CreatedAt,
                afterDispatchId: page.Single().DispatchId));
            Assert.Single(rest);
            var admitted = await store.TryAdmitAsync(first.DispatchId, DateTimeOffset.UtcNow);
            Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admitted.Disposition);
            var cancellation = new WorkflowDispatchCancellationRequest(
                second.DispatchId,
                second.ParentWorkflowExecutionId,
                second.ParentActivityExecutionId,
                second.ChildWorkflowExecutionId,
                DateTimeOffset.UtcNow);
            Assert.Equal(WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission, (await store.ApplyCancellationAsync(cancellation)).Disposition);
            Assert.Equal(WorkflowDispatchStatus.Cancelled, (await store.FindAsync(second.DispatchId))!.Status);
            var cancelled = await store.FindAsync(second.DispatchId);
            Assert.NotNull(cancelled);
            Assert.True(await store.TryDeleteAsync(cancelled!));
            Assert.Null(await store.FindAsync(second.DispatchId));
        }

        await using var reopened = createContext(fixture.ConnectionString);
        Assert.Equal(expectedProvider, reopened.Database.ProviderName);
        Assert.Null(await new EfWorkflowDispatchStore(reopened, new FixedAccessor(scope)).FindAsync("missing-dispatch"));
    }

    private static WorkflowDispatchRecord Pending(string parent, string activity, string tenant, DateTimeOffset createdAt)
    {
        var identity = new WorkflowDispatchIdentity(parent, activity);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity($"artifact-{activity}", "definition-child", "version-child", "1", $"hash-{activity}"),
            new WorkflowExecutableSourceProvenance($"source-{activity}", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            tenant,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            createdAt,
            createdAt,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" });
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
