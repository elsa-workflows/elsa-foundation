using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeActivityExecutionPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_runtime_activity_execution_model_crud_query_transaction_and_concurrency() =>
        RuntimeActivityExecutionProviderSmoke.RunAsync(fixture, "PostgreSql", connection => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options), RuntimePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeActivityExecutionSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_runtime_activity_execution_model_crud_query_transaction_and_concurrency() =>
        RuntimeActivityExecutionProviderSmoke.RunAsync(fixture, "SqlServer", connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options), RuntimeSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeActivityExecutionMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_runtime_activity_execution_model_crud_query_transaction_and_concurrency() =>
        RuntimeActivityExecutionProviderSmoke.RunAsync(fixture, "MySql", connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options), RuntimeMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeActivityExecutionProviderSmoke
{
    private const string SigningKey = "ef-runtime-r07-r09-provider-signing-key-32-bytes";

    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, string providerName, Func<string, RuntimeDbContext> createContext, string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"provider-r07-r09-{Guid.NewGuid():N}";
        var workflow = $"workflow-{Guid.NewGuid():N}";
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey }));
        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var state = new EfActivityExecutionStateStore(context, new FixedAccessor(scope), codec);
            var inspection = new EfActivityExecutionInspectionStore(context, new FixedAccessor(scope), codec);
            var hierarchy = new EfActivityExecutionHierarchyStore(context, new FixedAccessor(scope), new Elsa.Workflows.Runtime.Services.ActivityExecutions.HmacActivityExecutionHierarchyCursorCodec(Options.Create(new Elsa.Workflows.Runtime.Services.ActivityExecutions.ActivityExecutionHierarchyCursorOptions { SigningKey = SigningKey })));

            await state.SaveAsync(State(workflow, "root", 1, null));
            await state.SaveAsync(State(workflow, "child", 2, "root"));
            Assert.Equal(2, await state.CountAsync(workflow));
            Assert.Equal("child", Assert.Single((await state.ListByParentPageAsync(new ActivityExecutionStateParentPageQuery(workflow, "root"))).Items).Execution.ActivityExecutionId);

            var stale = await context.ActivityExecutionStates.AsNoTracking().SingleAsync(row =>
                row.WorkflowExecutionIdHash == EfRelationalIdentity.Hash(workflow) &&
                row.ActivityExecutionIdHash == EfRelationalIdentity.Hash("root"));
            await state.SaveAsync(State(workflow, "root", 3, null));
            context.ChangeTracker.Clear();
            context.ActivityExecutionStates.Update(stale);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();

            var projection = Projection(workflow, "root", 1, "root", null, true);
            await inspection.SaveAsync(projection);
            var childProjection = Projection(workflow, "child", 2, "root", "root", false);
            var siblingProjection = Projection(workflow, "child-b", 2, "root", "root", false);
            await inspection.SaveAsync(childProjection);
            await inspection.SaveAsync(siblingProjection);
            var summaryFirst = await inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery(workflow, 1));
            Assert.Equal(3, summaryFirst.TotalCount);
            Assert.NotNull(summaryFirst.NextContinuationToken);
            await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(projection));
            await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(childProjection));
            await hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(siblingProjection));
            Assert.NotNull(await inspection.FindAsync(workflow, "root"));
            Assert.NotNull(await hierarchy.FindBoundaryAsync(workflow, "root"));
            var hierarchyFirst = await hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
                workflow,
                "root",
                null,
                1,
                new HashSet<ActivityExecutionHierarchyInclude>(),
                "provider-smoke",
                $"tenant:{scope}"));
            Assert.NotNull(hierarchyFirst?.NextCursor);
            var hierarchySecond = await hierarchy.ReadPageAsync(new ActivityExecutionHierarchyQuery(
                workflow,
                "root",
                hierarchyFirst!.NextCursor,
                1,
                new HashSet<ActivityExecutionHierarchyInclude>(),
                "provider-smoke",
                $"tenant:{scope}"));
            Assert.Single(hierarchySecond!.Items);

            await using var transaction = await context.Database.BeginTransactionAsync();
            await state.SaveAsync(State(workflow, "rolled-back", 3, null));
            await transaction.RollbackAsync();
        }
        await using (var verify = createContext(fixture.ConnectionString))
        {
            var state = new EfActivityExecutionStateStore(verify, new FixedAccessor(scope), codec);
            Assert.Null(await state.FindAsync(workflow, "rolled-back"));
        }
    }

    private static ActivityExecutionState State(string workflow, string id, long sequence, string? parent = null) => new(
        new ActivityExecution(id, workflow, $"node-{id}", $"authored-{id}", "Test.Activity", "1"), ActivityExecutionStatus.Completed, null, sequence,
        DateTimeOffset.UnixEpoch.AddSeconds(sequence), null, DateTimeOffset.UnixEpoch.AddSeconds(sequence), parent, parent, null, null,
        ActivitySchedulingProvenance.From(workflow, parent, parent, null, null, null, parent, "test"), null, [], [], 0, 0, new Dictionary<string, string>(), ExecutionScopeId: parent);

    private static ActivityExecutionInspectionProjection Projection(string workflow, string id, long sequence, string scope, string? parent, bool boundary) => new(
        id, workflow, $"node-{id}", $"authored-{id}", "Test.Activity", "1", ActivityExecutionStatus.Completed, null, sequence,
        DateTimeOffset.UnixEpoch.AddSeconds(sequence), null, DateTimeOffset.UnixEpoch.AddSeconds(sequence), "checkpoint", "checkpoint", DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        ActivitySchedulingProvenance.From(workflow, parent, parent, null, null, null, scope, "test"), ["Done"], [], [], [], boundary ? new Dictionary<string, string>
        {
            ["activity.definitionId"] = "definition", ["activity.definitionVersionId"] = "version", ["activity.version"] = "1", ["activity.templateHash"] = "template"
        } : new Dictionary<string, string>(), scope);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
