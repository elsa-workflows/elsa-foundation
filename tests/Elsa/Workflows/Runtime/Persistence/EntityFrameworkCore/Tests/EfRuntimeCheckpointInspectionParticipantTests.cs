using System.Reflection;
using System.Runtime.ExceptionServices;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointInspectionParticipantTests
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Upsert_stages_inspection_hierarchy_and_sibling_in_the_callers_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var projection = Projection("workflow-a", "activity-a", 1, "root-a", boundary: true);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, projection), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        var inspection = new EfActivityExecutionInspectionStore(verification.Context, new FixedAccessor("tenant-a"), RecoveryCodec());
        var hierarchy = new EfActivityExecutionHierarchyStore(verification.Context, new FixedAccessor("tenant-a"), HierarchyCodec());
        Assert.NotNull(await inspection.FindAsync("workflow-a", "activity-a"));
        Assert.NotNull(await hierarchy.FindBoundaryAsync("workflow-a", "activity-a"));
        Assert.Single(await verification.Context.SchedulerStates.ToArrayAsync());
    }

    [Fact]
    public async Task Rollback_discards_inspection_hierarchy_and_sibling_without_independent_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var fixture = database.Open("tenant-a"))
        {
            await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert,
                Projection("workflow-a", "activity-a", 1, "root-a")), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verification = database.Open("tenant-a");
        Assert.Empty(await verification.Context.ActivityExecutionInspections.ToArrayAsync());
        Assert.Empty(await verification.Context.ActivityExecutionHierarchies.ToArrayAsync());
        Assert.Empty(await verification.Context.SchedulerStates.ToArrayAsync());
    }

    [Fact]
    public async Task Upsert_without_effective_execution_scope_retracts_existing_hierarchy()
    {
        await using var database = await TestDatabase.CreateAsync();
        var scoped = Projection("workflow-a", "activity-a", 1, "root-a");
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Inspection.SaveAsync(scoped);
            await seed.Hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(scoped));
        }

        var unscoped = scoped with
        {
            ExecutionScopeId = null,
            Provenance = ActivitySchedulingProvenance.From(
                scoped.WorkflowExecutionId,
                scoped.Provenance.ParentActivityExecutionId,
                scoped.Provenance.SchedulingActivityExecutionId,
                scoped.Provenance.BranchId,
                scoped.Provenance.IterationId,
                null,
                null,
                "test")
        };
        await using (var fixture = database.Open("tenant-a"))
        {
            await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, unscoped), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        Assert.NotNull(await verification.Inspection.FindAsync("workflow-a", "activity-a"));
        Assert.Null(await verification.Hierarchy.FindBoundaryAsync("workflow-a", "activity-a"));
    }

    [Fact]
    public async Task Staging_uses_revision_concurrency_and_rejects_projection_drift()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = Projection("workflow-a", "activity-a", 1, "root-a");
        await using (var seed = database.Open("tenant-a"))
            await seed.Inspection.SaveAsync(original);

        await using var stale = database.Open("tenant-a");
        _ = await stale.Context.ActivityExecutionInspections.SingleAsync();
        await using (var winner = database.Open("tenant-a"))
            await winner.Inspection.SaveAsync(original with { ExecutionSequence = 2 });

        await using (var transaction = await stale.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(stale.Context, Change(RuntimeStateChangeOperation.Upsert,
                original with { ExecutionSequence = 3 }), "tenant-a", "workflow-a");
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }

        await using (var tampered = database.Open("tenant-a"))
        {
            var row = await tampered.Context.ActivityExecutionInspections.SingleAsync();
            row.WorkflowExecutionId = EfRelationalIdentity.Encode("workflow-tampered");
            await tampered.Context.SaveChangesAsync();
            tampered.Context.ChangeTracker.Clear();
            await using var transaction = await tampered.Context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => StageAsync(tampered.Context,
                Change(RuntimeStateChangeOperation.Upsert, original with { ExecutionSequence = 4 }), "tenant-a", "workflow-a"));
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Staging_rejects_tampered_hierarchy_projection_after_inspection_is_staged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = Projection("workflow-a", "activity-a", 1, "root-a");
        await using (var seed = database.Open("tenant-a"))
        {
            await seed.Inspection.SaveAsync(original);
            await seed.Hierarchy.SaveAsync(ActivityExecutionHierarchyProjector.FromInspection(original));
        }

        await using var tampered = database.Open("tenant-a");
        var hierarchyRow = await tampered.Context.ActivityExecutionHierarchies.SingleAsync();
        hierarchyRow.WorkflowExecutionId = EfRelationalIdentity.Encode("workflow-tampered");
        await tampered.Context.SaveChangesAsync();
        tampered.Context.ChangeTracker.Clear();

        await using var transaction = await tampered.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => StageAsync(
            tampered.Context,
            Change(RuntimeStateChangeOperation.Upsert, original with { ExecutionSequence = 2 }),
            "tenant-a",
            "workflow-a"));
        await transaction.RollbackAsync();

        tampered.Context.ChangeTracker.Clear();
        var persistedInspection = await tampered.Context.ActivityExecutionInspections.SingleAsync();
        Assert.Equal(1, persistedInspection.SummaryExecutionSequence);
        Assert.True(persistedInspection.Revision > 0);
    }

    [Fact]
    public async Task Staging_requires_a_caller_owned_transaction_and_applies_upserts_only()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var projection = Projection("workflow-a", "activity-a", 1, "root-a");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, projection), "tenant-a", "workflow-a"));

        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        var delete = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, projection), "tenant-a", "workflow-a"));
        Assert.Equal("The EF checkpoint writer can only project activity execution inspection upserts.", delete.Message);
        await transaction.RollbackAsync();
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
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
        string? parent = null,
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
            ActivitySchedulingProvenance.From(workflow, parent, parent, null, null, null, executionScope, "test"),
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
        BookmarkStateDbContext context,
        RuntimeStateChange<ActivityExecutionInspectionProjection> change,
        string scope,
        string workflowExecutionId)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointInspectionParticipantStaging")!;
        var method = type.GetMethod("StageAsync", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            var result = (ValueTask)method.Invoke(null, [context, change, scope, workflowExecutionId, CancellationToken.None])!;
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

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r19-inspection-participant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public TestFixture Open(string scope) => new(connectionString, scope);
        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public BookmarkStateSqliteDbContext Context { get; }
        public EfActivityExecutionInspectionStore Inspection { get; }
        public EfActivityExecutionHierarchyStore Hierarchy { get; }

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            var accessor = new FixedAccessor(scope);
            Inspection = new(Context, accessor, RecoveryCodec());
            Hierarchy = new(Context, accessor, HierarchyCodec());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
