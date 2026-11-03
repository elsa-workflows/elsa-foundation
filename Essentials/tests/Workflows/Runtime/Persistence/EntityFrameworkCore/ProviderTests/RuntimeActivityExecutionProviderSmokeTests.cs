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
            var summarySecond = await inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery(workflow, 1, summaryFirst.NextContinuationToken));
            var summaryThird = await inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery(workflow, 1, summarySecond.NextContinuationToken));
            Assert.Equal(3, summarySecond.TotalCount);
            Assert.Equal(3, summaryThird.TotalCount);
            Assert.NotNull(summarySecond.NextContinuationToken);
            Assert.Null(summaryThird.NextContinuationToken);
            Assert.Equal("root", summaryFirst.Items.Single().ActivityExecutionId);
            Assert.Equal(new[] { "child", "child-b" }, new[]
            {
                summarySecond.Items.Single().ActivityExecutionId,
                summaryThird.Items.Single().ActivityExecutionId
            }.OrderBy(id => id, StringComparer.Ordinal));
            var browserPage = await inspection.BrowseAsync(new ActivityExecutionBrowseQuery(workflow, 1,
                parentActivityExecutionId: "root", ancestorActivityExecutionId: "root"));
            Assert.Equal(2, browserPage.TotalCount);
            Assert.Equal(2, Assert.Single(browserPage.Items).Position);
            Assert.Equal("child-b", browserPage.Items[0].Execution.ActivityExecutionId);
            var browserAnchor = await inspection.BrowseAsync(new ActivityExecutionBrowseQuery(workflow, 1,
                executableNodeId: childProjection.ExecutableNodeId, ancestorActivityExecutionId: "root", anchorActivityExecutionId: "child"));
            Assert.Equal("child", Assert.Single(browserAnchor.Items).Execution.ActivityExecutionId);
            Assert.Equal(1, browserAnchor.TotalCount);
            var absentAnchor = await inspection.BrowseAsync(new ActivityExecutionBrowseQuery(workflow,
                executableNodeId: childProjection.ExecutableNodeId, anchorActivityExecutionId: "child-b"));
            Assert.Empty(absentAnchor.Items);
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

            var rowsAfterSummaryCursor = await context.ActivityExecutionInspections
                .Where(row => row.WorkflowExecutionIdHash == EfRelationalIdentity.Hash(workflow) &&
                              (row.ActivityExecutionIdHash == EfRelationalIdentity.Hash("child") ||
                               row.ActivityExecutionIdHash == EfRelationalIdentity.Hash("child-b")))
                .ToArrayAsync();
            Assert.Equal(2, rowsAfterSummaryCursor.Length);
            context.ActivityExecutionInspections.RemoveRange(rowsAfterSummaryCursor);
            await context.SaveChangesAsync();
            var emptySummaryPage = await inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery(workflow, 1, summaryFirst.NextContinuationToken));
            Assert.Equal(1, emptySummaryPage.TotalCount);
            Assert.Empty(emptySummaryPage.Items);
            Assert.Null(emptySummaryPage.NextContinuationToken);

            await using (var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
            {
                await inspection.SaveAsync(Projection(workflow, "rolled-back-inspection", 4, "root", null, false));
                var transactionPage = await inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery(workflow));
                Assert.Equal(2, transactionPage.TotalCount);
                Assert.Contains(transactionPage.Items, item => item.ActivityExecutionId == "rolled-back-inspection");
                Assert.Same(transaction, context.Database.CurrentTransaction);
                await state.SaveAsync(State(workflow, "rolled-back-state", 3, null));
                await transaction.RollbackAsync();
            }

            await using (var readCommitted = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted))
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await inspection.ListSummariesPageAsync(new ActivityExecutionInspectionSummaryPageQuery(workflow)));
                Assert.Contains("Serializable or Snapshot", exception.Message, StringComparison.Ordinal);
                Assert.Same(readCommitted, context.Database.CurrentTransaction);
                await state.SaveAsync(State(workflow, "read-committed-rolled-back", 3, null));
                await readCommitted.RollbackAsync();
            }
        }
        await using (var verify = createContext(fixture.ConnectionString))
        {
            var state = new EfActivityExecutionStateStore(verify, new FixedAccessor(scope), codec);
            var inspection = new EfActivityExecutionInspectionStore(verify, new FixedAccessor(scope), codec);
            Assert.Null(await state.FindAsync(workflow, "rolled-back-state"));
            Assert.Null(await state.FindAsync(workflow, "read-committed-rolled-back"));
            Assert.Null(await inspection.FindAsync(workflow, "rolled-back-inspection"));
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
