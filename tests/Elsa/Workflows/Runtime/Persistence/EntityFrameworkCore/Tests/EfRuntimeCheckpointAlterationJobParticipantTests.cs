using System.Reflection;
using System.Runtime.ExceptionServices;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointAlterationJobParticipantTests
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Terminal_change_is_staged_with_a_sibling_and_committed_atomically()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = await database.CreateRunningJobAsync("tenant-a");
        var change = TerminalChange(fixture.Job);

        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", fixture.Job.WorkflowExecutionId));
        await StageAsync(fixture.Context, change, "tenant-a", fixture.Job.WorkflowExecutionId);
        await fixture.Context.SaveChangesAsync();
        await transaction.CommitAsync();

        await using var verification = database.Open("tenant-a");
        var saved = (await verification.Store.FindJobAsync(fixture.Job.JobId))!;
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, saved.Status);
        Assert.Equal(fixture.Job.Revision + 1, saved.Revision);
        Assert.Equal(change.CheckpointCommitId, saved.CheckpointCommitId);
        Assert.Single(await verification.Context.SchedulerStates.ToArrayAsync());
    }

    [Fact]
    public async Task Rollback_leaves_running_job_and_sibling_unchanged_then_replay_is_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = await database.CreateRunningJobAsync("tenant-a");
        var change = TerminalChange(fixture.Job);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", fixture.Job.WorkflowExecutionId));
            await StageAsync(fixture.Context, change, "tenant-a", fixture.Job.WorkflowExecutionId);
            await fixture.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.Equal(WorkflowAlterationJobStatus.Running, (await fixture.Store.FindJobAsync(fixture.Job.JobId))!.Status);
        Assert.Empty(await fixture.Context.SchedulerStates.ToArrayAsync());
        fixture.Context.ChangeTracker.Clear();

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, change, "tenant-a", fixture.Job.WorkflowExecutionId);
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, change, "tenant-a", fixture.Job.WorkflowExecutionId);
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var replayed = (await fixture.Store.FindJobAsync(fixture.Job.JobId))!;
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, replayed.Status);
        Assert.Equal(fixture.Job.Revision + 1, replayed.Revision);
    }

    [Fact]
    public async Task Staging_requires_transaction_and_rejects_wrong_workflow_or_claim_before_mutation()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = await database.CreateRunningJobAsync("tenant-a");
        var change = TerminalChange(fixture.Job);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, change, "tenant-a", fixture.Job.WorkflowExecutionId));

        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() =>
            StageAsync(fixture.Context, change, "tenant-a", "workflow-other"));
        await Assert.ThrowsAsync<WorkflowAlterationClaimFenceException>(() =>
            StageAsync(fixture.Context, new WorkflowAlterationJobTerminalChange(
                change.JobId,
                "stale-claim",
                change.Status,
                change.Outcomes,
                change.CheckpointCommitId,
                change.CompletedAt,
                change.SafeFailure), "tenant-a", fixture.Job.WorkflowExecutionId));
        await transaction.RollbackAsync();

        var unchanged = (await fixture.Store.FindJobAsync(fixture.Job.JobId))!;
        Assert.Equal(WorkflowAlterationJobStatus.Running, unchanged.Status);
        Assert.Equal(fixture.Job.Revision, unchanged.Revision);
    }

    [Fact]
    public async Task Staging_uses_revision_cas_and_rejects_projection_drift()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = await database.CreateRunningJobAsync("tenant-a");
        var change = TerminalChange(fixture.Job);

        await using var stale = database.Open("tenant-a");
        _ = await stale.Context.WorkflowAlterationJobs.SingleAsync();
        await using (var winner = database.Open("tenant-a"))
            await winner.Store.ApplyTerminalJobChangeAsync(change);

        await using (var transaction = await stale.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(stale.Context, change, "tenant-a", fixture.Job.WorkflowExecutionId);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }

        await using var tampered = database.Open("tenant-a");
        var row = await tampered.Context.WorkflowAlterationJobs.SingleAsync();
        row.JobId = EfRelationalIdentity.Encode("other-job");
        await tampered.Context.SaveChangesAsync();
        await using var transaction2 = await tampered.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            StageAsync(tampered.Context, change, "tenant-a", fixture.Job.WorkflowExecutionId));
        await transaction2.RollbackAsync();
    }

    [Fact]
    public async Task Terminal_replay_with_conflicting_evidence_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = await database.CreateRunningJobAsync("tenant-a");
        var change = TerminalChange(fixture.Job);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, change, "tenant-a", fixture.Job.WorkflowExecutionId);
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        fixture.Context.ChangeTracker.Clear();
        var conflicting = new WorkflowAlterationJobTerminalChange(
            change.JobId,
            change.ClaimToken,
            WorkflowAlterationJobStatus.Failed,
            change.Outcomes,
            change.CheckpointCommitId,
            change.CompletedAt,
            change.SafeFailure);
        await using var replayTransaction = await fixture.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, conflicting, "tenant-a", fixture.Job.WorkflowExecutionId));
        await replayTransaction.RollbackAsync();

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.Store.FindJobAsync(fixture.Job.JobId))!.Status);
    }

    private static WorkflowAlterationJobTerminalChange TerminalChange(WorkflowAlterationJobState job) =>
        new(
            job.JobId,
            job.Claim!.Token,
            WorkflowAlterationJobStatus.Succeeded,
            [new WorkflowAlterationOutcome(0, "test", 1, WorkflowAlterationOutcomeStatus.Succeeded, "ok", null, CapturedAt)],
            WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, 1),
            CapturedAt.AddMinutes(1));

    private static WorkflowAlterationPlanState Plan(string scope) =>
        WorkflowAlterationPlanState.CreateCapturing(
            "plan-" + Guid.NewGuid().ToString("N"),
            new(scope, "system", "root"),
            new("subject", "correlation"),
            "idem-" + Guid.NewGuid().ToString("N"),
            "canonical-" + Guid.NewGuid().ToString("N"),
            new("key", "AES", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["workflow-a"]),
            CapturedAt);

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
        WorkflowAlterationJobTerminalChange change,
        string scope,
        string workflowExecutionId)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointAlterationJobParticipantStaging")!;
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

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r19-alteration-participant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public TestFixture Open(string scope) => new(connectionString, scope);

        public async Task<TestFixture> CreateRunningJobAsync(string scope)
        {
            var fixture = Open(scope);
            var plan = Plan(scope);
            await fixture.Store.AdmitAsync(plan);
            await fixture.Store.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget("workflow-a", scope)], null);
            await fixture.Store.SealAsync(plan.PlanId, 1, CapturedAt.AddSeconds(1));
            fixture.Job = (await fixture.Store.ClaimNextAsync(plan.PlanId, "worker", CapturedAt.AddSeconds(2), TimeSpan.FromMinutes(1)))!;
            return fixture;
        }

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public BookmarkStateSqliteDbContext Context { get; }
        public EfWorkflowAlterationStore Store { get; }
        public WorkflowAlterationJobState Job { get; set; } = null!;

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            Store = new(Context, new FixedAccessor(scope), Codec());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private static IRuntimeRecoveryContinuationCodec Codec() =>
        new HmacRuntimeRecoveryContinuationCodec(
            Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r19-alteration-participant-signing-key-32-bytes" }));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
