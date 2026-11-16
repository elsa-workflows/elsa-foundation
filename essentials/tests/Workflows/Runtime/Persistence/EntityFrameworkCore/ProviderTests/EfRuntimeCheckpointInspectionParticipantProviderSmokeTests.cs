using System.Reflection;
using System.Runtime.ExceptionServices;
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
public sealed class RuntimeCheckpointInspectionParticipantPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_checkpoint_inspection_participant_smoke() =>
        RuntimeCheckpointInspectionParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
            RuntimePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointInspectionParticipantSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_checkpoint_inspection_participant_smoke() =>
        RuntimeCheckpointInspectionParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
            RuntimeSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointInspectionParticipantMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_checkpoint_inspection_participant_smoke() =>
        RuntimeCheckpointInspectionParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeCheckpointInspectionParticipantProviderSmoke
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, RuntimeDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r19-inspections-{Guid.NewGuid():N}";
        var projection = Projection("workflow-a", "activity-a", 1, "root-a", boundary: true);

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var inspection = new EfActivityExecutionInspectionStore(context, new FixedAccessor(scope), RecoveryCodec());
            var hierarchy = new EfActivityExecutionHierarchyStore(context, new FixedAccessor(scope), HierarchyCodec());

            await using var transaction = await context.Database.BeginTransactionAsync();
            context.SchedulerStates.Add(SchedulerRow(scope, "workflow-a"));
            await StageAsync(context, Change(RuntimeStateChangeOperation.Upsert, projection), scope, "workflow-a");
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            Assert.NotNull(await inspection.FindAsync("workflow-a", "activity-a"));
            Assert.NotNull(await hierarchy.FindBoundaryAsync("workflow-a", "activity-a"));
        }

        await using (var context = createContext(fixture.ConnectionString))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await StageAsync(context, Change(RuntimeStateChangeOperation.Upsert, projection with { ExecutionSequence = 2 }), scope, "workflow-a");
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            var committed = await new EfActivityExecutionInspectionStore(verification, new FixedAccessor(scope), RecoveryCodec()).FindAsync("workflow-a", "activity-a");
            Assert.Equal(1, committed?.ExecutionSequence);
            Assert.NotNull(await new EfActivityExecutionHierarchyStore(verification, new FixedAccessor(scope), HierarchyCodec()).FindBoundaryAsync("workflow-a", "activity-a"));
            Assert.Single(await verification.SchedulerStates.Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).ToArrayAsync());
        }

        var conflictScope = $"{scope}-conflict";
        await using (var seed = createContext(fixture.ConnectionString))
            await new EfActivityExecutionInspectionStore(seed, new FixedAccessor(conflictScope), RecoveryCodec()).SaveAsync(projection);
        await using var stale = createContext(fixture.ConnectionString);
        _ = await stale.ActivityExecutionInspections.SingleAsync(row => row.ScopeKeyHash == EfRelationalIdentity.Hash(conflictScope));
        await using (var winner = createContext(fixture.ConnectionString))
            await new EfActivityExecutionInspectionStore(winner, new FixedAccessor(conflictScope), RecoveryCodec()).SaveAsync(projection with { ExecutionSequence = 2 });
        await using var conflictTransaction = await stale.Database.BeginTransactionAsync();
        await StageAsync(stale, Change(RuntimeStateChangeOperation.Upsert, projection with { ExecutionSequence = 3 }), conflictScope, "workflow-a");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await conflictTransaction.RollbackAsync();
    }

    private static RuntimeStateChange<ActivityExecutionInspectionProjection> Change(
        RuntimeStateChangeOperation operation,
        ActivityExecutionInspectionProjection projection) =>
        new(projection.ActivityExecutionId, operation, projection, new Dictionary<string, string>());

    private static ActivityExecutionInspectionProjection Projection(
        string workflow,
        string activity,
        long sequence,
        string executionScope,
        bool boundary = false) =>
        new(
            activity,
            workflow,
            $"node-{activity}",
            $"authored-{activity}",
            "Test.Activity",
            "1",
            ActivityExecutionStatus.Completed,
            null,
            sequence,
            CapturedAt,
            CapturedAt,
            CapturedAt,
            "checkpoint-1",
            "checkpoint-1",
            CapturedAt,
            ActivitySchedulingProvenance.From(workflow, null, null, null, null, null, executionScope, "test"),
            ["Done"],
            [],
            [],
            [],
            boundary ? new Dictionary<string, string>
            {
                ["activity.definitionId"] = "definition",
                ["activity.definitionVersionId"] = "version",
                ["activity.version"] = "1",
                ["activity.templateHash"] = "template"
            } : new Dictionary<string, string>(),
            executionScope);

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

    private static async Task StageAsync(
        RuntimeDbContext context,
        RuntimeStateChange<ActivityExecutionInspectionProjection> change,
        string scope,
        string workflowExecutionId)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointInspectionParticipantStaging")!;
        var method = type.GetMethod("StageAsync", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            var result = (ValueTask)method.Invoke(null, [context, new[] { change }, scope, workflowExecutionId, CancellationToken.None])!;
            await result.AsTask();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static IRuntimeRecoveryContinuationCodec RecoveryCodec() =>
        new Elsa.Workflows.Runtime.Services.Recovery.HmacRuntimeRecoveryContinuationCodec(
            Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r19-inspection-signing-key-32-bytes" }));

    private static IActivityExecutionHierarchyCursorCodec HierarchyCodec() =>
        new Elsa.Workflows.Runtime.Services.ActivityExecutions.HmacActivityExecutionHierarchyCursorCodec(
            Microsoft.Extensions.Options.Options.Create(new Elsa.Workflows.Runtime.Services.ActivityExecutions.ActivityExecutionHierarchyCursorOptions { SigningKey = "ef-r19-hierarchy-signing-key-32-bytes" }));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
