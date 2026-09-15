using Elsa.Persistence.EntityFramework;
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
public sealed class RuntimeWorkflowDispatchTestScopedPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_test_scoped_dispatch_admission_model_and_transaction() => RuntimeWorkflowDispatchTestScopedProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
        BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeWorkflowDispatchTestScopedSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_test_scoped_dispatch_admission_model_and_transaction() => RuntimeWorkflowDispatchTestScopedProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
        BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeWorkflowDispatchTestScopedMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_test_scoped_dispatch_admission_model_and_transaction() => RuntimeWorkflowDispatchTestScopedProviderSmoke.RunAsync(
        fixture,
        connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
        BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeWorkflowDispatchTestScopedProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"dispatch-testscope-native-{Guid.NewGuid():N}";
        var testScope = new WorkflowTestScope(
            "native-scope",
            Now.AddHours(1),
            scope,
            new WorkflowExecutionPartition("native-partition"));

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var access = new FixedAccessor(scope);
            var codec = new HmacRuntimeRecoveryContinuationCodec(
                Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));
            var scopeStore = new EfWorkflowTestScopeStore(context, access, codec);
            var dispatchStore = new EfWorkflowDispatchStore(context, access);
            await scopeStore.CreateAsync(testScope, Now);
            var dispatch = Pending(testScope);
            await dispatchStore.SaveAsync(dispatch);

            var admitted = await dispatchStore.TryAdmitAsync(dispatch.DispatchId, Now.AddMinutes(1));

            Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admitted.Disposition);
            Assert.Equal(WorkflowDispatchStatus.Started, admitted.Record.Status);
            Assert.Equal(1, await context.WorkflowTestScopes.AsNoTracking()
                .Where(row => row.ScopeId == EfRelationalIdentity.Encode(testScope.ScopeId))
                .Select(row => row.Revision).SingleAsync());
        }

        await using var reopened = createContext(fixture.ConnectionString);
        Assert.Equal(expectedProvider, reopened.Database.ProviderName);
        var restarted = new EfWorkflowDispatchStore(reopened, new FixedAccessor(scope));
        var dispatchId = new WorkflowDispatchIdentity("native-parent", "native-activity").DispatchId;
        Assert.Equal(WorkflowDispatchStatus.Started, (await restarted.FindAsync(dispatchId))!.Status);
    }

    private static WorkflowDispatchRecord Pending(WorkflowTestScope testScope)
    {
        const string parent = "native-parent";
        const string activity = "native-activity";
        var identity = new WorkflowDispatchIdentity(parent, activity);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("native-artifact", "definition-child", "version-child", "1", "native-hash"),
            new WorkflowExecutableSourceProvenance("native-source", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            testScope.TenantId,
            testScope.Partition,
            WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "native-initiator"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            Now,
            Now,
            new Dictionary<string, string> { ["native"] = "test-scoped-admission" },
            testScope: testScope);
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
