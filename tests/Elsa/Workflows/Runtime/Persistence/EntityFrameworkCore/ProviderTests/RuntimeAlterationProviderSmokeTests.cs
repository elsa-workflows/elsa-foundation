using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeAlterationPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_runtime_alteration_and_test_scope_smoke() =>
        RuntimeAlterationProviderSmoke.RunAsync(
            fixture,
            c => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(c).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName,
            "PostgreSql");
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeAlterationSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_runtime_alteration_and_test_scope_smoke() =>
        RuntimeAlterationProviderSmoke.RunAsync(
            fixture,
            c => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(c).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName,
            "SqlServer");
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeAlterationMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_runtime_alteration_and_test_scope_smoke() =>
        RuntimeAlterationProviderSmoke.RunAsync(
            fixture,
            c => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(c).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName,
            "MySql");
}

internal static class RuntimeAlterationProviderSmoke
{
    private const string SigningKey = "ef-runtime-r11-r13-provider-signing-key-32-bytes";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider,
        string providerName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");

        var connectionString = fixture.ConnectionString;
        var scope = $"native-{Guid.NewGuid():N}";
        var codec = new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey }));
        var now = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();

            var accessor = new FixedAccessor(scope);
            var alterationStore = new EfWorkflowAlterationStore(context, accessor, codec);
            var scopeStore = new EfWorkflowTestScopeStore(context, accessor, codec);

            await AlterationLifecycleAsync(alterationStore, scope, now);
            await ScopeLifecycleAndQueryAsync(scopeStore, scope, now);
        }

        await RollbackIsInvisibleAfterRestartAsync(createContext, connectionString, codec, scope, now);
        await OptimisticConcurrencyIsProviderCompatibleAsync(createContext, connectionString, codec, scope, now);
    }

    private static async Task AlterationLifecycleAsync(
        EfWorkflowAlterationStore store,
        string scope,
        DateTimeOffset now)
    {
        var plan = WorkflowAlterationPlanState.CreateCapturing(
            "native-plan-" + Guid.NewGuid().ToString("N"),
            new(scope, "system", "root"),
            new("subject", null),
            "idem-" + Guid.NewGuid().ToString("N"),
            "canonical-" + Guid.NewGuid().ToString("N"),
            new("key", "AES", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["execution"]),
            now);
        await store.AdmitAsync(plan);

        var second = WorkflowAlterationPlanState.CreateCapturing(
            "native-plan-" + Guid.NewGuid().ToString("N"),
            new(scope, "system", "root"),
            new("subject", null),
            "idem-" + Guid.NewGuid().ToString("N"),
            "canonical-" + Guid.NewGuid().ToString("N"),
            new("key", "AES", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["execution-2"]),
            now);
        await store.AdmitAsync(second);

        await store.AdmitAsync(WorkflowAlterationPlanState.CreateCapturing(
            "aa",
            new(scope, "system", "root"),
            new("subject", null),
            "idem-aa-" + Guid.NewGuid().ToString("N"),
            "canonical-aa-" + Guid.NewGuid().ToString("N"),
            new("key", "AES", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["execution-aa"]),
            now));
        await store.AdmitAsync(WorkflowAlterationPlanState.CreateCapturing(
            "aG",
            new(scope, "system", "root"),
            new("subject", null),
            "idem-aG-" + Guid.NewGuid().ToString("N"),
            "canonical-aG-" + Guid.NewGuid().ToString("N"),
            new("key", "AES", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["execution-aG"]),
            now));

        var activePage = await store.ListActivePlansAsync(1);
        Assert.True(activePage.HasNext);
        Assert.Single((await store.ListActivePlansAsync(1, activePage.NextCursor)).Items);

        var seenCaseSensitiveIds = new HashSet<string>(StringComparer.Ordinal);
        string? activeCursor = null;
        do
        {
            var page = await store.ListActivePlansAsync(1, activeCursor);
            foreach (var item in page.Items.Where(item => item.PlanId is "aa" or "aG"))
                Assert.True(seenCaseSensitiveIds.Add(item.PlanId));
            activeCursor = page.NextCursor;
        } while (activeCursor is not null);
        Assert.Equal(["aG", "aa"], seenCaseSensitiveIds.Order(StringComparer.Ordinal));

        await store.CaptureAsync(
            plan.PlanId,
            0,
            [new WorkflowAlterationCapturedTarget("execution", scope)],
            null);
        var jobs = await store.PageJobsAsync(plan.PlanId, 1);
        Assert.False(jobs.HasNext);
        await store.SealAsync(plan.PlanId, 1, now.AddSeconds(1));
        Assert.NotNull(await store.ClaimNextAsync(plan.PlanId, "worker", now.AddSeconds(2), TimeSpan.FromMinutes(1)));
    }

    private static async Task ScopeLifecycleAndQueryAsync(
        EfWorkflowTestScopeStore store,
        string scope,
        DateTimeOffset now)
    {
        var lifecycleScope = new WorkflowTestScope(
            "native-lifecycle-" + Guid.NewGuid().ToString("N"),
            now.AddHours(1),
            scope,
            new WorkflowExecutionPartition("native-partition"));
        var created = await store.CreateAsync(lifecycleScope, now);
        await store.AssertOpenAsync(lifecycleScope, now.AddSeconds(1));

        var openPage = await store.QueryAsync(
            new WorkflowTestScopePageQuery(now.AddSeconds(1), 10, WorkflowTestScopeState.Open));
        Assert.Contains(openPage.Items, record => record.Scope.ScopeId == lifecycleScope.ScopeId);

        var firstClosingScope = new WorkflowTestScope(
            "native-query-a-" + Guid.NewGuid().ToString("N"),
            now.AddHours(1),
            scope,
            new WorkflowExecutionPartition("native-partition"));
        var secondClosingScope = new WorkflowTestScope(
            "native-query-b-" + Guid.NewGuid().ToString("N"),
            now.AddHours(1),
            scope,
            new WorkflowExecutionPartition("native-partition"));
        await store.CreateAsync(firstClosingScope, now);
        await store.CreateAsync(secondClosingScope, now);
        await store.CloseAsync(new WorkflowTestScopeCloseRequest(firstClosingScope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, now.AddMinutes(1)));
        await store.CloseAsync(new WorkflowTestScopeCloseRequest(secondClosingScope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, now.AddMinutes(1)));

        var closingPage = await store.QueryAsync(
            new WorkflowTestScopePageQuery(now.AddMinutes(1), 1, WorkflowTestScopeState.Closing));
        Assert.Single(closingPage.Items);
        Assert.NotNull(closingPage.ContinuationToken);
        var closingPageAfterCursor = await store.QueryAsync(
            new WorkflowTestScopePageQuery(now.AddMinutes(1), 1, WorkflowTestScopeState.Closing, closingPage.ContinuationToken));
        Assert.Single(closingPageAfterCursor.Items);
        Assert.NotEqual(closingPage.Items[0].Scope.ScopeId, closingPageAfterCursor.Items[0].Scope.ScopeId);

        var close = await store.CloseAsync(new WorkflowTestScopeCloseRequest(lifecycleScope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, created.CreatedAt.AddMinutes(1)));
        Assert.Equal(WorkflowTestScopeCloseDisposition.Accepted, close.Disposition);
        var completed = await store.CompleteAsync(lifecycleScope.ScopeId, created.CreatedAt.AddMinutes(2));
        Assert.Equal(WorkflowTestScopeState.Closed, completed.State);
        Assert.Equal(WorkflowTestScopeCloseDisposition.AlreadyClosed, (await store.CloseAsync(new WorkflowTestScopeCloseRequest(lifecycleScope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, created.CreatedAt.AddMinutes(3)))).Disposition);
        Assert.Equal(WorkflowTestScopeState.Closed, (await store.CompleteAsync(lifecycleScope.ScopeId, created.CreatedAt.AddMinutes(3))).State);
    }

    private static async Task RollbackIsInvisibleAfterRestartAsync(
        Func<string, BookmarkStateDbContext> createContext,
        string connectionString,
        IRuntimeRecoveryContinuationCodec codec,
        string scope,
        DateTimeOffset now)
    {
        var rolledBackScope = new WorkflowTestScope(
            "native-rollback-" + Guid.NewGuid().ToString("N"),
            now.AddHours(1),
            scope,
            new WorkflowExecutionPartition("native-partition"));

        await using (var transactionContext = createContext(connectionString))
        {
            var accessor = new FixedAccessor(scope);
            var scopeStore = new EfWorkflowTestScopeStore(transactionContext, accessor, codec);
            await using var transaction = await transactionContext.Database.BeginTransactionAsync();
            await scopeStore.CreateAsync(rolledBackScope, now);
            await transaction.RollbackAsync();
        }

        await using var verificationContext = createContext(connectionString);
        var verificationStore = new EfWorkflowTestScopeStore(verificationContext, new FixedAccessor(scope), codec);
        Assert.Null(await verificationStore.FindAsync(rolledBackScope.ScopeId));
    }

    private static async Task OptimisticConcurrencyIsProviderCompatibleAsync(
        Func<string, BookmarkStateDbContext> createContext,
        string connectionString,
        IRuntimeRecoveryContinuationCodec codec,
        string scope,
        DateTimeOffset now)
    {
        var plan = WorkflowAlterationPlanState.CreateCapturing(
            "native-concurrency-plan-" + Guid.NewGuid().ToString("N"),
            new(scope, "system", "root"),
            new("subject", null),
            "idem-" + Guid.NewGuid().ToString("N"),
            "canonical-" + Guid.NewGuid().ToString("N"),
            new("key", "AES", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["execution"]),
            now);
        await using (var seedContext = createContext(connectionString))
            await new EfWorkflowAlterationStore(seedContext, new FixedAccessor(scope), codec).AdmitAsync(plan);

        await using (var leftContext = createContext(connectionString))
        await using (var rightContext = createContext(connectionString))
        {
            var leftPlan = await leftContext.WorkflowAlterationPlans.SingleAsync(row => row.PlanId == EfRelationalIdentity.Encode(plan.PlanId));
            var rightPlan = await rightContext.WorkflowAlterationPlans.SingleAsync(row => row.PlanId == EfRelationalIdentity.Encode(plan.PlanId));
            leftPlan.ActiveOrderKey = $"{now.AddMinutes(1).UtcTicks:D19}:{Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(plan.PlanId, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength))}";
            await leftContext.SaveChangesAsync();
            rightPlan.ActiveOrderKey = $"{now.AddMinutes(2).UtcTicks:D19}:{Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(plan.PlanId, RuntimeWorkflowAlterationEfModule.IdentityMaximumLength))}";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => rightContext.SaveChangesAsync());
        }

        var scopeRecord = new WorkflowTestScope(
            "native-concurrency-scope-" + Guid.NewGuid().ToString("N"),
            now.AddHours(1),
            scope,
            new WorkflowExecutionPartition("native-partition"));
        await using (var seedContext = createContext(connectionString))
            await new EfWorkflowTestScopeStore(seedContext, new FixedAccessor(scope), codec).CreateAsync(scopeRecord, now);

        await using (var leftContext = createContext(connectionString))
        await using (var rightContext = createContext(connectionString))
        {
            var leftStore = new EfWorkflowTestScopeStore(leftContext, new FixedAccessor(scope), codec);
            var rightScope = await rightContext.WorkflowTestScopes.SingleAsync(row => row.ScopeId == EfRelationalIdentity.Encode(scopeRecord.ScopeId));
            await leftStore.AssertOpenAsync(scopeRecord, now.AddMinutes(1));
            rightScope.Revision++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => rightContext.SaveChangesAsync());
        }
    }

    private sealed class FixedAccessor(string value) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(value));
    }
}
