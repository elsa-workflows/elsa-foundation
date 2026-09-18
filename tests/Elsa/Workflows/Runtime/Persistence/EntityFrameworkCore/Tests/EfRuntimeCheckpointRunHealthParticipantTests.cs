using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointRunHealthParticipantTests
{
    private static readonly DateTimeOffset StartedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

    [Fact]
    public async Task New_workflow_creates_health_projection_and_counts_unique_new_incidents()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var workflow = Workflow("workflow-a", WorkflowExecutionStatus.Running, WorkflowRunKind.PublishedRun);
        var incidentA = Incident("incident-a", workflow.WorkflowExecutionId);
        var incidentB = Incident("incident-b", workflow.WorkflowExecutionId);

        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        fixture.Context.WorkflowExecutionStates.Add(WorkflowEntity(workflow, "tenant-a"));
        fixture.Context.IncidentStates.Add(IncidentEntity(incidentA, "tenant-a"));
        fixture.Context.IncidentStates.Add(IncidentEntity(incidentB, "tenant-a"));
        await StageAsync(
            fixture.Context,
            workflow.WorkflowExecutionId,
            Change(RuntimeStateChangeOperation.Upsert, workflow),
            [Change(RuntimeStateChangeOperation.Append, incidentA), Change(RuntimeStateChangeOperation.Append, incidentB)],
            "tenant-a");
        await fixture.Context.SaveChangesAsync();
        await transaction.CommitAsync();

        await using var verification = database.Open("tenant-a");
        var row = await verification.Context.WorkflowRunHealthStates.SingleAsync();
        Assert.Equal(EfRelationalIdentity.Encode("tenant-a"), row.ScopeKey);
        Assert.Equal(EfRelationalIdentity.Encode(workflow.WorkflowExecutionId), row.WorkflowExecutionId);
        Assert.Equal(EfRelationalIdentity.Encode(workflow.PinnedExecutable.DefinitionId), row.DefinitionId);
        Assert.Equal((int)workflow.RunKind, row.RunKind);
        Assert.Equal((int)workflow.Status, row.Status);
        Assert.Equal(StartedAt.UtcTicks, row.StartedAtUtcTicks);
        Assert.Equal((int)StartedAt.Offset.TotalMinutes, row.StartedAtOffsetMinutes);
        Assert.Equal(2, row.IncidentCount);
        Assert.Equal(1, row.IncidentBearingCount);
    }

    [Fact]
    public async Task Workflow_updates_preserve_original_start_and_incident_updates_do_not_recount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var workflow = Workflow("workflow-a", WorkflowExecutionStatus.Running, WorkflowRunKind.PublishedRun);
        await using (var seed = database.Open("tenant-a"))
        {
            await using var transaction = await seed.Context.Database.BeginTransactionAsync();
            seed.Context.WorkflowExecutionStates.Add(WorkflowEntity(workflow, "tenant-a"));
            await StageAsync(seed.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow), [], "tenant-a");
            await seed.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var incident = Incident("incident-a", workflow.WorkflowExecutionId);
        await using (var update = database.Open("tenant-a"))
        {
            await using var transaction = await update.Context.Database.BeginTransactionAsync();
            update.Context.IncidentStates.Add(IncidentEntity(incident, "tenant-a"));
            var changedWorkflow = workflow with { Status = WorkflowExecutionStatus.Suspended, StartedAt = StartedAt.AddDays(2) };
            await StageAsync(update.Context, changedWorkflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, changedWorkflow), [Change(RuntimeStateChangeOperation.Append, incident)], "tenant-a");
            await update.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var repeated = database.Open("tenant-a"))
        {
            await using var transaction = await repeated.Context.Database.BeginTransactionAsync();
            var changedWorkflow = workflow with { Status = WorkflowExecutionStatus.Completed, StartedAt = StartedAt.AddDays(3) };
            await StageAsync(repeated.Context, changedWorkflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, changedWorkflow), [Change(RuntimeStateChangeOperation.Upsert, incident)], "tenant-a");
            await repeated.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        var row = await verification.Context.WorkflowRunHealthStates.SingleAsync();
        Assert.Equal((int)WorkflowExecutionStatus.Completed, row.Status);
        Assert.Equal(StartedAt.UtcTicks, row.StartedAtUtcTicks);
        Assert.Equal(1, row.IncidentCount);
        Assert.Equal(1, row.IncidentBearingCount);
    }

    [Fact]
    public async Task Incident_only_checkpoint_requires_existing_workflow_and_health_projection()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var incident = Incident("incident-a", "workflow-a");
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, "workflow-a", null, [Change(RuntimeStateChangeOperation.Append, incident)], "tenant-a"));

        Assert.Contains("requires the workflow and its run-health projection", exception.Message, StringComparison.Ordinal);
        await transaction.RollbackAsync();
        Assert.Empty(await fixture.Context.WorkflowRunHealthStates.ToArrayAsync());
    }

    [Fact]
    public async Task New_workflow_rejects_an_orphaned_persisted_health_projection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var workflow = Workflow("workflow-a", WorkflowExecutionStatus.Pending, WorkflowRunKind.PublishedRun);
        await using (var seed = database.Open("tenant-a"))
        {
            seed.Context.WorkflowRunHealthStates.Add(HealthEntity(workflow, "tenant-a"));
            await seed.Context.SaveChangesAsync();
        }

        await using var fixture = database.Open("tenant-a");
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        fixture.Context.WorkflowExecutionStates.Add(WorkflowEntity(workflow, "tenant-a"));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow), [], "tenant-a"));

        Assert.Contains("already has a run-health projection", exception.Message, StringComparison.Ordinal);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Duplicate_incident_ids_are_rejected_before_health_folding()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var workflow = Workflow("workflow-a", WorkflowExecutionStatus.Running, WorkflowRunKind.PublishedRun);
        var incident = Incident("incident-a", workflow.WorkflowExecutionId);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        fixture.Context.WorkflowExecutionStates.Add(WorkflowEntity(workflow, "tenant-a"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(
                fixture.Context,
                workflow.WorkflowExecutionId,
                Change(RuntimeStateChangeOperation.Upsert, workflow),
                [Change(RuntimeStateChangeOperation.Append, incident), Change(RuntimeStateChangeOperation.Upsert, incident)],
                "tenant-a"));

        Assert.Contains("occurs more than once", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Context.WorkflowRunHealthStates.Local);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Rollback_discards_health_and_new_incident_then_retry_replays_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var workflow = Workflow("workflow-a", WorkflowExecutionStatus.Running, WorkflowRunKind.TestRun);
        await using (var seed = database.Open("tenant-a"))
        {
            await using var transaction = await seed.Context.Database.BeginTransactionAsync();
            seed.Context.WorkflowExecutionStates.Add(WorkflowEntity(workflow, "tenant-a"));
            await StageAsync(seed.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow), [], "tenant-a");
            await seed.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var incident = Incident("incident-a", workflow.WorkflowExecutionId);
        await using (var failed = database.Open("tenant-a"))
        {
            await using var transaction = await failed.Context.Database.BeginTransactionAsync();
            failed.Context.IncidentStates.Add(IncidentEntity(incident, "tenant-a"));
            await StageAsync(failed.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow), [Change(RuntimeStateChangeOperation.Append, incident)], "tenant-a");
            await failed.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var retry = database.Open("tenant-a"))
        {
            await using var transaction = await retry.Context.Database.BeginTransactionAsync();
            retry.Context.IncidentStates.Add(IncidentEntity(incident, "tenant-a"));
            await StageAsync(retry.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow), [Change(RuntimeStateChangeOperation.Append, incident)], "tenant-a");
            await retry.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        Assert.Equal(1, await verification.Context.IncidentStates.CountAsync());
        Assert.Equal(1, await verification.Context.WorkflowRunHealthStates.Select(row => row.IncidentCount).SingleAsync());
    }

    [Fact]
    public async Task Existing_health_projection_drift_fails_closed_before_staging()
    {
        await using var database = await TestDatabase.CreateAsync();
        var workflow = Workflow("workflow-a", WorkflowExecutionStatus.Running, WorkflowRunKind.PublishedRun);
        await using (var seed = database.Open("tenant-a"))
        {
            await using var transaction = await seed.Context.Database.BeginTransactionAsync();
            seed.Context.WorkflowExecutionStates.Add(WorkflowEntity(workflow, "tenant-a"));
            await StageAsync(seed.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow), [], "tenant-a");
            await seed.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var corrupt = database.Open("tenant-a"))
        {
            var row = await corrupt.Context.WorkflowRunHealthStates.SingleAsync();
            row.DefinitionId = EfRelationalIdentity.Encode("definition-corrupt");
            await corrupt.Context.SaveChangesAsync();
        }

        await using var fixture = database.Open("tenant-a");
        await using var transaction2 = await fixture.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            StageAsync(fixture.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow), [], "tenant-a"));
        await transaction2.RollbackAsync();
    }

    [Fact]
    public async Task Concurrent_health_update_uses_revision_compare_and_swap()
    {
        await using var database = await TestDatabase.CreateAsync();
        var workflow = Workflow("workflow-a", WorkflowExecutionStatus.Running, WorkflowRunKind.PublishedRun);
        await using (var seed = database.Open("tenant-a"))
        {
            await using var transaction = await seed.Context.Database.BeginTransactionAsync();
            seed.Context.WorkflowExecutionStates.Add(WorkflowEntity(workflow, "tenant-a"));
            await StageAsync(seed.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow), [], "tenant-a");
            await seed.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var stale = database.Open("tenant-a");
        _ = await stale.Context.WorkflowRunHealthStates.SingleAsync();
        await using (var winner = database.Open("tenant-a"))
        {
            await using var transaction = await winner.Context.Database.BeginTransactionAsync();
            var changed = workflow with { Status = WorkflowExecutionStatus.Suspended };
            await StageAsync(winner.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, changed), [], "tenant-a");
            await winner.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var staleTransaction = await stale.Context.Database.BeginTransactionAsync();
        await StageAsync(stale.Context, workflow.WorkflowExecutionId, Change(RuntimeStateChangeOperation.Upsert, workflow with { Status = WorkflowExecutionStatus.Completed }), [], "tenant-a");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
        await staleTransaction.RollbackAsync();

        await using var verification = database.Open("tenant-a");
        Assert.Equal((int)WorkflowExecutionStatus.Suspended, await verification.Context.WorkflowRunHealthStates.Select(row => row.Status).SingleAsync());
    }

    private static RuntimeStateChange<T> Change<T>(RuntimeStateChangeOperation operation, T state, string? id = null) =>
        new(id ?? StateId(state), operation, state, new Dictionary<string, string>());

    private static string StateId<T>(T state) => state switch
    {
        WorkflowExecutionState workflow => workflow.WorkflowExecutionId,
        IncidentState incident => incident.IncidentId,
        _ => throw new InvalidOperationException()
    };

    private static WorkflowExecutionState Workflow(string id, WorkflowExecutionStatus status, WorkflowRunKind runKind) =>
        new(
            id,
            new WorkflowExecutableIdentity($"artifact-{id}", $"definition-{id}", "version-1", "1", $"hash-{id}"),
            status,
            null,
            StartedAt.AddDays(-1),
            StartedAt,
            StartedAt,
            status.IsTerminal() ? StartedAt.AddHours(1) : null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>())
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

    private static IncidentStateEntity IncidentEntity(IncidentState state, string scope)
    {
        var id = CompositeId(scope, state.WorkflowExecutionId, state.IncidentId);
        return new IncidentStateEntity
        {
            Id = id,
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
    }

    private static WorkflowRunHealthStateEntity HealthEntity(WorkflowExecutionState state, string scope)
    {
        var assembly = typeof(EfWorkflowExecutionStateStore).Assembly;
        var projectionType = assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.WorkflowRunHealthProjection")!;
        var projection = Activator.CreateInstance(
            projectionType,
            state.WorkflowExecutionId,
            state.PinnedExecutable.DefinitionId,
            state.RunKind,
            state.StartedAt,
            state.Status,
            0L,
            0L)!;
        var stagingType = assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointRunHealthParticipantStaging")!;
        var id = CompositeId(scope, state.WorkflowExecutionId);
        var toEntity = stagingType.GetMethod("ToEntity", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (WorkflowRunHealthStateEntity)toEntity.Invoke(null, [projection, scope, id, 1L])!;
    }

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
        RuntimeDbContext context,
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

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r19-run-health-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(
                new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public TestFixture Open(string scope) => new(connectionString, scope);
        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public RuntimeSqliteDbContext Context { get; }

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new RuntimeSqliteDbContext(
                new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
