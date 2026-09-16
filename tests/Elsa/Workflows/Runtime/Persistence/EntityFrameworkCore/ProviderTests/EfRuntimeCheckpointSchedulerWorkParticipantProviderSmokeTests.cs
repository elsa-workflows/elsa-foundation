using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeCheckpointSchedulerWorkParticipantPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_checkpoint_scheduler_work_participant_smoke() =>
        RuntimeCheckpointSchedulerWorkParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointSchedulerWorkParticipantSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_checkpoint_scheduler_work_participant_smoke() =>
        RuntimeCheckpointSchedulerWorkParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointSchedulerWorkParticipantMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_checkpoint_scheduler_work_participant_smoke() =>
        RuntimeCheckpointSchedulerWorkParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeCheckpointSchedulerWorkParticipantProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private const string SigningKey = "ef-runtime-r22-checkpoint-participant-provider-key";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r22-checkpoint-{Guid.NewGuid():N}";
        RuntimeSchedulerWorkClaim rollbackClaim;

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);

            await store.EnqueueAsync(Work("workflow-success", "work-success"));
            var successClaim = (await store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                "workflow-success", "owner-a", Now, TimeSpan.FromMinutes(1))))!;
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.SchedulerStates.Add(SchedulerRow(scope, "workflow-success"));
                await StageConsumeAsync(context, ConsumedSchedulerWorkItem.FromClaim(successClaim), scope);
                context.RuntimeCheckpointCommits.Add(Marker(scope, "commit-success", "workflow-success", "work-success"));
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            await store.EnqueueAsync(Work("workflow-rollback", "work-rollback"));
            rollbackClaim = (await store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                "workflow-rollback", "owner-a", Now, TimeSpan.FromMinutes(1))))!;
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.SchedulerStates.Add(SchedulerRow(scope, "workflow-rollback"));
                await StageConsumeAsync(context, ConsumedSchedulerWorkItem.FromClaim(rollbackClaim), scope);
                context.RuntimeCheckpointCommits.Add(Marker(scope, "commit-rollback", "workflow-rollback", "work-rollback"));
                await context.SaveChangesAsync();
                await transaction.RollbackAsync();
            }
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            Assert.Empty(await verification.SchedulerWorkItems.Where(row => row.WorkflowExecutionId == EfRelationalIdentity.Encode("workflow-success")).ToArrayAsync());
            Assert.Single(await verification.SchedulerStates.Where(row => row.WorkflowExecutionId == EfRelationalIdentity.Encode("workflow-success")).ToArrayAsync());
            Assert.Single(await verification.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("commit-success")).ToArrayAsync());
            var rollbackRow = await verification.SchedulerWorkItems.SingleAsync(row => row.WorkflowExecutionId == EfRelationalIdentity.Encode("workflow-rollback"));
            Assert.Equal(EfRelationalIdentity.Encode("owner-a"), rollbackRow.ClaimOwnerId);
            Assert.Equal(rollbackClaim.FencingToken, rollbackRow.ClaimToken);
            Assert.Empty(await verification.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("commit-rollback")).ToArrayAsync());
        }

        await using (var successorContext = createContext(fixture.ConnectionString))
        {
            var successor = await Store(successorContext, scope).ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                "workflow-rollback", "owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1)));
            Assert.NotNull(successor);
            Assert.True(successor!.FencingToken > rollbackClaim.FencingToken);
        }

        await using (var context = createContext(fixture.ConnectionString))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() =>
                StageConsumeAsync(context, ConsumedSchedulerWorkItem.FromClaim(rollbackClaim), scope));
            await transaction.RollbackAsync();
        }
    }

    private static EfSchedulerWorkQueueStore Store(BookmarkStateDbContext context, string scope) =>
        new(context, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(
            Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));

    private static RuntimeSchedulerWorkItem Work(string workflowExecutionId, string workItemId) =>
        new(workItemId, workflowExecutionId, "command", WorkflowExecutionCommandKind.ScheduleActivity,
            "envelope", "idempotency", Now, Now, 1);

    private static async Task StageConsumeAsync(
        BookmarkStateDbContext context,
        ConsumedSchedulerWorkItem consumed,
        string scope)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointParticipantStaging")!;
        var method = type.GetMethod("StageConsumedSchedulerWorkAsync", BindingFlags.Public | BindingFlags.Static)!;

        try
        {
            var result = (ValueTask)method.Invoke(null, [context, consumed, scope, CancellationToken.None])!;
            await result.AsTask();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

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

    private static RuntimeCheckpointCommitEntity Marker(
        string scope,
        string commitId,
        string workflowExecutionId,
        string workItemId) => new()
    {
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{commitId.Length}:{commitId}"),
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        CommitId = EfRelationalIdentity.Encode(commitId),
        CommitIdHash = EfRelationalIdentity.Hash(commitId),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
        WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        OccurredAtUtcTicks = Now.UtcTicks,
        Fingerprint = new string('a', 64),
        ContentJson = "{}",
        PendingPostCommitWorkIdsJson = "[]",
        ConsumedSchedulerWorkItemIdsJson = JsonSerializer.Serialize(new[] { workItemId }),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
