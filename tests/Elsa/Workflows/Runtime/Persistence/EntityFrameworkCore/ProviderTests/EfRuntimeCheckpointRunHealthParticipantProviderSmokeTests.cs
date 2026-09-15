using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeCheckpointRunHealthParticipantPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_checkpoint_run_health_model_transaction_and_concurrency() =>
        RuntimeCheckpointRunHealthParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointRunHealthParticipantSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_checkpoint_run_health_model_transaction_and_concurrency() =>
        RuntimeCheckpointRunHealthParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointRunHealthParticipantMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_checkpoint_run_health_model_transaction_and_concurrency() =>
        RuntimeCheckpointRunHealthParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeCheckpointRunHealthParticipantProviderSmoke
{
    private static readonly DateTimeOffset StartedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r19-run-health-{Guid.NewGuid():N}";
        var workflow = Workflow("workflow-a", WorkflowExecutionStatus.Running, WorkflowRunKind.PublishedRun, scope);
        var incident = Incident("incident-a", workflow.WorkflowExecutionId);

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.WorkflowExecutionStates.Add(WorkflowEntity(workflow, scope));
            context.IncidentStates.Add(IncidentEntity(incident, scope));
            await StageAsync(context, workflow.WorkflowExecutionId, Change(workflow), [Change(incident)], scope);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            var row = await verification.WorkflowRunHealthStates.SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(scope));
            Assert.Equal((int)WorkflowRunKind.PublishedRun, row.RunKind);
            Assert.Equal((int)WorkflowExecutionStatus.Running, row.Status);
            Assert.Equal(1, row.IncidentCount);
            Assert.Equal(1, row.IncidentBearingCount);
        }

        await using (var rollback = createContext(fixture.ConnectionString))
        {
            await using var transaction = await rollback.Database.BeginTransactionAsync();
            await StageAsync(rollback, workflow.WorkflowExecutionId, Change(workflow with { Status = WorkflowExecutionStatus.Faulted }), [], scope);
            await rollback.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var stale = createContext(fixture.ConnectionString))
        {
            _ = await stale.WorkflowRunHealthStates.SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(scope));
            await using (var winner = createContext(fixture.ConnectionString))
            {
                await using var transaction = await winner.Database.BeginTransactionAsync();
                await StageAsync(winner, workflow.WorkflowExecutionId, Change(workflow with { Status = WorkflowExecutionStatus.Suspended }), [], scope);
                await winner.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            await using var staleTransaction = await stale.Database.BeginTransactionAsync();
            await StageAsync(stale, workflow.WorkflowExecutionId, Change(workflow with { Status = WorkflowExecutionStatus.Completed }), [], scope);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            await staleTransaction.RollbackAsync();
        }

        await using var final = createContext(fixture.ConnectionString);
        Assert.Equal((int)WorkflowExecutionStatus.Suspended,
            await final.WorkflowRunHealthStates.Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).Select(row => row.Status).SingleAsync());
    }

    private static RuntimeStateChange<WorkflowExecutionState> Change(WorkflowExecutionState state) =>
        new(state.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>());

    private static RuntimeStateChange<IncidentState> Change(IncidentState state) =>
        new(state.IncidentId, RuntimeStateChangeOperation.Append, state, new Dictionary<string, string>());

    private static WorkflowExecutionState Workflow(string id, WorkflowExecutionStatus status, WorkflowRunKind runKind, string scope) =>
        new(id, new WorkflowExecutableIdentity($"artifact-{id}", $"definition-{id}", "version-1", "1", $"hash-{id}"), status, null,
            StartedAt.AddMinutes(-1), StartedAt, StartedAt, null, null, null, scope, new Dictionary<string, string>())
        { RunKind = runKind };

    private static IncidentState Incident(string id, string workflowExecutionId) =>
        new(id, workflowExecutionId, null, null, IncidentSeverity.Error, IncidentStatus.Open, null, "failure", $"message-{id}", StartedAt, null);

    private static WorkflowExecutionStateEntity WorkflowEntity(WorkflowExecutionState state, string scope)
    {
        var type = typeof(EfWorkflowExecutionStateStore);
        var createId = type.GetMethod("CreateId", BindingFlags.NonPublic | BindingFlags.Static)!;
        var id = (string)createId.Invoke(null, [scope, state.WorkflowExecutionId])!;
        var toEntity = type.GetMethod("ToEntity", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (WorkflowExecutionStateEntity)toEntity.Invoke(null, [state, scope, id, 1L])!;
    }

    private static IncidentStateEntity IncidentEntity(IncidentState state, string scope) => new()
    {
        Id = CompositeId(scope, state.WorkflowExecutionId, state.IncidentId),
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        WorkflowExecutionId = EfRelationalIdentity.Encode(state.WorkflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(state.WorkflowExecutionId),
        WorkflowExecutionIdOrderKey = Order(state.WorkflowExecutionId),
        IncidentId = EfRelationalIdentity.Encode(state.IncidentId),
        IncidentIdHash = EfRelationalIdentity.Hash(state.IncidentId),
        IncidentIdOrderKey = Order(state.IncidentId),
        Status = (int)state.Status,
        Severity = (int)state.Severity,
        CreatedAtUtcTicks = state.CreatedAt.UtcTicks,
        ContentJson = SerializeRuntime(state),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static string SerializeRuntime<T>(T value)
    {
        var type = typeof(EfWorkflowExecutionStateStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.RuntimeArtifactJson")!;
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name == "Serialize" && candidate.IsGenericMethodDefinition);
        return (string)method.MakeGenericMethod(typeof(T)).Invoke(null, [value])!;
    }

    private static string CompositeId(params string[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
            builder.Append(value.Length).Append(':').Append(value);
        return EfRelationalIdentity.Hash(builder.ToString());
    }

    private static string Order(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));

    private static async Task StageAsync(
        BookmarkStateDbContext context,
        string workflowExecutionId,
        RuntimeStateChange<WorkflowExecutionState>? workflowChange,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> incidentChanges,
        string scope)
    {
        var type = typeof(EfWorkflowExecutionStateStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointRunHealthParticipantStaging")!;
        var method = type.GetMethod("StageWorkflowRunHealthAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            var result = (ValueTask)method.Invoke(null, [context, workflowExecutionId, workflowChange, incidentChanges, scope, CancellationToken.None])!;
            await result.AsTask();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
