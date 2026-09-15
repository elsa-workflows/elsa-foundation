using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
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
public sealed class RuntimeCheckpointDurableValueParticipantPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_checkpoint_durable_value_participant_smoke() =>
        RuntimeCheckpointDurableValueParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointDurableValueParticipantSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_checkpoint_durable_value_participant_smoke() =>
        RuntimeCheckpointDurableValueParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointDurableValueParticipantMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_checkpoint_durable_value_participant_smoke() =>
        RuntimeCheckpointDurableValueParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeCheckpointDurableValueParticipantProviderSmoke
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r19-durable-values-{Guid.NewGuid():N}";
        var original = Value("value-a", "workflow-a", "before");

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);
            await store.SaveAsync(original);

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.SchedulerStates.Add(SchedulerRow(scope, "workflow-a"));
                await StageAsync(context, Change(RuntimeStateChangeOperation.Upsert, Value("value-a", "workflow-a", "committed")), scope);
                context.RuntimeCheckpointCommits.Add(Marker(scope, "commit-value", "workflow-a"));
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            Assert.Equal("committed", (await store.FindAsync("workflow-a", "value-a"))!.InlineValue!.Value.GetString());

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await StageAsync(context, Change(RuntimeStateChangeOperation.Delete, Value("value-a", "workflow-a", "committed")), scope);
                await context.SaveChangesAsync();
                await transaction.RollbackAsync();
            }
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            Assert.Equal("committed", (await Store(verification, scope).FindAsync("workflow-a", "value-a"))!.InlineValue!.Value.GetString());
            Assert.Single(await verification.SchedulerStates.Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).ToArrayAsync());
            Assert.Single(await verification.RuntimeCheckpointCommits.Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).ToArrayAsync());
        }

        await using (var tenantB = createContext(fixture.ConnectionString))
            await Store(tenantB, $"{scope}-other").SaveAsync(original);

        await using (var tenantA = createContext(fixture.ConnectionString))
        {
            await using var transaction = await tenantA.Database.BeginTransactionAsync();
            await StageAsync(tenantA, Change(RuntimeStateChangeOperation.Delete, Value("value-a", "workflow-a", "committed")), scope);
            await tenantA.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var isolation = createContext(fixture.ConnectionString))
        {
            Assert.Null(await Store(isolation, scope).FindAsync("workflow-a", "value-a"));
            Assert.NotNull(await Store(isolation, $"{scope}-other").FindAsync("workflow-a", "value-a"));
        }

        var conflictScope = $"{scope}-conflict";
        await using (var seed = createContext(fixture.ConnectionString))
            await Store(seed, conflictScope).SaveAsync(Value("value-c", "workflow-c", "before"));
        await using var stale = createContext(fixture.ConnectionString);
        _ = await stale.DurableValueStates.SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(conflictScope));
        await using (var winner = createContext(fixture.ConnectionString))
            await Store(winner, conflictScope).SaveAsync(Value("value-c", "workflow-c", "winner"));
        await using var conflictTransaction = await stale.Database.BeginTransactionAsync();
        await StageAsync(stale, Change(RuntimeStateChangeOperation.Upsert, Value("value-c", "workflow-c", "stale")), conflictScope);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await conflictTransaction.RollbackAsync();
        await using var conflictVerification = createContext(fixture.ConnectionString);
        Assert.Equal("winner", (await Store(conflictVerification, conflictScope).FindAsync("workflow-c", "value-c"))!.InlineValue!.Value.GetString());
    }

    private static EfDurableValueStateStore Store(BookmarkStateDbContext context, string scope) =>
        new(context, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(
            Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "r19-durable-value-provider-smoke-signing-key" })));

    private static RuntimeStateChange<DurableValueState> Change(RuntimeStateChangeOperation operation, DurableValueState state) =>
        new(state.DurableValueId, operation, state, new Dictionary<string, string>());

    private static DurableValueState Value(string durableValueId, string workflowExecutionId, string value) =>
        new(durableValueId, workflowExecutionId, durableValueId, new RuntimeValueTypeDescriptor("json", null, null), DurableValueLifecycle.Result, DurableValueStorage.Inline,
            JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement, null, null, CapturedAt, new Dictionary<string, string>());

    private static async Task StageAsync(BookmarkStateDbContext context, RuntimeStateChange<DurableValueState> change, string scope)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointParticipantStaging")!;
        var method = type.GetMethod("StageDurableValueAsync", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            var result = (ValueTask)method.Invoke(null, [context, change, scope, CancellationToken.None])!;
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
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{workflowExecutionId.Length}:{workflowExecutionId}"), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId), WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId), WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        Collection = "schedulerState", ContentJson = "{}", SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = 1
    };

    private static RuntimeCheckpointCommitEntity Marker(string scope, string commitId, string workflowExecutionId) => new()
    {
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{commitId.Length}:{commitId}"), ScopeKey = EfRelationalIdentity.Encode(scope), ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        CommitId = EfRelationalIdentity.Encode(commitId), CommitIdHash = EfRelationalIdentity.Hash(commitId), CommitIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(commitId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId), WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId), WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        OccurredAtUtcTicks = CapturedAt.UtcTicks, Fingerprint = new string('a', 64), ContentJson = "{}", PendingPostCommitWorkIdsJson = "[]", ConsumedSchedulerWorkItemIdsJson = "[]",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = 1
    };

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
