using System.Reflection;
using System.Runtime.ExceptionServices;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class EfRuntimeCheckpointActivityExecutionParticipantPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_runtime_checkpoint_activity_execution_transaction_and_concurrency() =>
        EfRuntimeCheckpointActivityExecutionParticipantProviderSmoke.RunAsync(
            fixture,
            "PostgreSql",
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class EfRuntimeCheckpointActivityExecutionParticipantSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_runtime_checkpoint_activity_execution_transaction_and_concurrency() =>
        EfRuntimeCheckpointActivityExecutionParticipantProviderSmoke.RunAsync(
            fixture,
            "SqlServer",
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class EfRuntimeCheckpointActivityExecutionParticipantMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_runtime_checkpoint_activity_execution_transaction_and_concurrency() =>
        EfRuntimeCheckpointActivityExecutionParticipantProviderSmoke.RunAsync(
            fixture,
            "MySql",
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class EfRuntimeCheckpointActivityExecutionParticipantProviderSmoke
{
    private const string SigningKey = "ef-r19-activity-provider-smoke-signing-key-32-bytes";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        string providerName,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProviderName)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? $"Docker/{providerName} is unavailable.");
        var scope = $"provider-r19-activity-{Guid.NewGuid():N}";
        var workflow = $"workflow-{Guid.NewGuid():N}";
        var activity = $"activity-{Guid.NewGuid():N}";
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey }));

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.SchedulerStates.Add(SchedulerRow(scope, workflow));
                await StageAsync(context, Change(RuntimeStateChangeOperation.Append, State(workflow, activity, 1)), scope, workflow);
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await StageAsync(context, Change(RuntimeStateChangeOperation.Upsert, State(workflow, activity, 2)), scope, workflow);
                await context.SaveChangesAsync();
                await transaction.RollbackAsync();
            }
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            var store = new EfActivityExecutionStateStore(verification, new FixedAccessor(scope), codec);
            Assert.Equal(1, (await store.FindAsync(workflow, activity))!.ExecutionSequence);
            Assert.Single(await verification.SchedulerStates.Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).ToArrayAsync());
        }

        await using var stale = createContext(fixture.ConnectionString);
        _ = await stale.ActivityExecutionStates.SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(scope));
        await using (var winner = createContext(fixture.ConnectionString))
        {
            var store = new EfActivityExecutionStateStore(winner, new FixedAccessor(scope), codec);
            await store.SaveAsync(State(workflow, activity, 3));
        }

        await using (var transaction = await stale.Database.BeginTransactionAsync())
        {
            await StageAsync(stale, Change(RuntimeStateChangeOperation.Upsert, State(workflow, activity, 4)), scope, workflow);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
    }

    private static RuntimeStateChange<ActivityExecutionState> Change(RuntimeStateChangeOperation operation, ActivityExecutionState state) =>
        new(state.Execution.ActivityExecutionId, operation, state, new Dictionary<string, string>());

    private static ActivityExecutionState State(string workflowExecutionId, string activityExecutionId, long sequence) => new(
        new ActivityExecution(activityExecutionId, workflowExecutionId, $"node-{activityExecutionId}", $"authored-{activityExecutionId}", "Test.Activity", "1"),
        ActivityExecutionStatus.Completed,
        null,
        sequence,
        DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        null,
        null,
        null,
        null,
        ActivitySchedulingProvenance.From(workflowExecutionId, null, null, null, null, null, null, "provider-smoke"),
        null,
        [],
        [],
        0,
        0,
        new Dictionary<string, string>());

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
        RuntimeStateChange<ActivityExecutionState> change,
        string scope,
        string workflowExecutionId)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointActivityExecutionParticipantStaging")!;
        var method = type.GetMethod("StageActivityExecutionAsync", BindingFlags.Public | BindingFlags.Static)!;
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

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
