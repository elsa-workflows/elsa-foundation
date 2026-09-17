using System.Reflection;
using System.Runtime.ExceptionServices;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeCheckpointAlterationJobParticipantPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_checkpoint_alteration_job_participant_smoke() =>
        RuntimeCheckpointAlterationJobParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointAlterationJobParticipantSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_checkpoint_alteration_job_participant_smoke() =>
        RuntimeCheckpointAlterationJobParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointAlterationJobParticipantMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_checkpoint_alteration_job_participant_smoke() =>
        RuntimeCheckpointAlterationJobParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeCheckpointAlterationJobParticipantProviderSmoke
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r19-alteration-{Guid.NewGuid():N}";
        WorkflowAlterationJobState committedJob;

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = new EfWorkflowAlterationStore(context, new FixedAccessor(scope), Codec());
            committedJob = await SeedRunningAsync(store, scope, "commit");
            var change = TerminalChange(committedJob);

            await using var transaction = await context.Database.BeginTransactionAsync();
            await StageAsync(context, change, scope, committedJob.WorkflowExecutionId);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            var saved = (await new EfWorkflowAlterationStore(verification, new FixedAccessor(scope), Codec()).FindJobAsync(committedJob.JobId))!;
            Assert.Equal(WorkflowAlterationJobStatus.Succeeded, saved.Status);
            Assert.Equal(committedJob.Revision + 1, saved.Revision);
        }

        await using (var callbackContext = createContext(fixture.ConnectionString))
        {
            var access = new FixedAccessor(scope);
            var alteration = new EfWorkflowAlterationStore(callbackContext, access, Codec());
            var job = await SeedRunningAsync(alteration, scope, "callback");
            var terminal = TerminalChange(job);
            var checkpoint = new RuntimeCheckpointCommit(terminal.CheckpointCommitId,
                new RuntimeCheckpoint($"checkpoint:{terminal.CheckpointCommitId}", "runtime.alteration.job",
                    job.WorkflowExecutionId, terminal.CompletedAt, [], new Dictionary<string, string>()),
                new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
                    null, null, null, null, null, null, terminal),
                [], new Dictionary<string, string>());
            var writer = new EfRuntimeCheckpointCommitStore(callbackContext, access);
            async ValueTask CommitCheckpointAsync(CancellationToken cancellationToken) =>
                _ = await writer.CommitAsync(checkpoint, new(RuntimeCheckpointPersistenceMode.Immediate), cancellationToken);

            await alteration.CommitTerminalJobChangeAtomicallyAsync(terminal, CommitCheckpointAsync);
            await alteration.CommitTerminalJobChangeAtomicallyAsync(terminal, CommitCheckpointAsync);
            Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await alteration.FindJobAsync(job.JobId))!.Status);
            Assert.Single(await callbackContext.RuntimeCheckpointCommits.Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope) && row.CommitId == EfRelationalIdentity.Encode(terminal.CheckpointCommitId)).ToArrayAsync());
        }

        await using var rollbackContext = createContext(fixture.ConnectionString);
        var rollbackStore = new EfWorkflowAlterationStore(rollbackContext, new FixedAccessor(scope), Codec());
        var rollbackJob = await SeedRunningAsync(rollbackStore, scope, "rollback");
        await using (var transaction = await rollbackContext.Database.BeginTransactionAsync())
        {
            await StageAsync(rollbackContext, TerminalChange(rollbackJob), scope, rollbackJob.WorkflowExecutionId);
            await rollbackContext.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var rollbackVerification = createContext(fixture.ConnectionString))
        {
            var saved = (await new EfWorkflowAlterationStore(rollbackVerification, new FixedAccessor(scope), Codec()).FindJobAsync(rollbackJob.JobId))!;
            Assert.Equal(WorkflowAlterationJobStatus.Running, saved.Status);
        }

        await using var staleContext = createContext(fixture.ConnectionString);
        var staleStore = new EfWorkflowAlterationStore(staleContext, new FixedAccessor(scope), Codec());
        var staleJob = await SeedRunningAsync(staleStore, scope, "cas");
        _ = await staleContext.WorkflowAlterationJobs.SingleAsync(row => row.JobId == EfRelationalIdentity.Encode(staleJob.JobId));
        var casChange = TerminalChange(staleJob);
        await using (var winnerContext = createContext(fixture.ConnectionString))
            await new EfWorkflowAlterationStore(winnerContext, new FixedAccessor(scope), Codec()).ApplyTerminalJobChangeAsync(casChange);

        await using (var transaction = await staleContext.Database.BeginTransactionAsync())
        {
            await StageAsync(staleContext, casChange, scope, staleJob.WorkflowExecutionId);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
    }

    private static async Task<WorkflowAlterationJobState> SeedRunningAsync(EfWorkflowAlterationStore store, string scope, string suffix)
    {
        var plan = WorkflowAlterationPlanState.CreateCapturing(
            $"plan-{suffix}-{Guid.NewGuid():N}",
            new(scope, "system", "root"),
            new("subject", "correlation"),
            $"idem-{suffix}-{Guid.NewGuid():N}",
            $"canonical-{suffix}-{Guid.NewGuid():N}",
            new("key", "AES", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["workflow-a"]),
            CapturedAt);
        await store.AdmitAsync(plan);
        await store.CaptureAsync(plan.PlanId, 0, [new WorkflowAlterationCapturedTarget("workflow-a", scope)], null);
        await store.SealAsync(plan.PlanId, 1, CapturedAt.AddSeconds(1));
        return (await store.ClaimNextAsync(plan.PlanId, "worker", CapturedAt.AddSeconds(2), TimeSpan.FromMinutes(1)))!;
    }

    private static WorkflowAlterationJobTerminalChange TerminalChange(WorkflowAlterationJobState job) =>
        new(
            job.JobId,
            job.Claim!.Token,
            WorkflowAlterationJobStatus.Succeeded,
            [new WorkflowAlterationOutcome(0, "test", 1, WorkflowAlterationOutcomeStatus.Succeeded, "ok", null, CapturedAt)],
            WorkflowAlterationIdentity.CreateCheckpointCommitId(job.JobId, 1),
            CapturedAt.AddMinutes(1));

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

    private static IRuntimeRecoveryContinuationCodec Codec() =>
        new HmacRuntimeRecoveryContinuationCodec(
            Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "ef-r19-alteration-native-signing-key-32-bytes" }));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
