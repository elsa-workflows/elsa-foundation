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
public sealed class RuntimeCheckpointIncidentParticipantPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_checkpoint_incident_participant_smoke() =>
        RuntimeCheckpointIncidentParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(connection).Options),
            BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointIncidentParticipantSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_checkpoint_incident_participant_smoke() =>
        RuntimeCheckpointIncidentParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(connection).Options),
            BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointIncidentParticipantMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_checkpoint_incident_participant_smoke() =>
        RuntimeCheckpointIncidentParticipantProviderSmoke.RunAsync(
            fixture,
            connection => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(connection).Options),
            BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeCheckpointIncidentParticipantProviderSmoke
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, BookmarkStateDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r19-incidents-{Guid.NewGuid():N}";
        var original = Incident("incident-a", "workflow-a", "before");

        await using (var context = createContext(fixture.ConnectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);
            Assert.True(await store.TryAddAsync(original));

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.SchedulerStates.Add(SchedulerRow(scope, "workflow-a"));
                await StageAsync(context, Change(RuntimeStateChangeOperation.Upsert, Incident("incident-a", "workflow-a", "committed")), scope, "workflow-a");
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            Assert.Equal("committed", (await store.FindAsync("workflow-a", "incident-a"))!.Message);
        }

        await using (var verification = createContext(fixture.ConnectionString))
        {
            Assert.Equal("committed", (await Store(verification, scope).FindAsync("workflow-a", "incident-a"))!.Message);
            Assert.Single(await verification.SchedulerStates.ToArrayAsync());

            await using var transaction = await verification.Database.BeginTransactionAsync();
            await StageAsync(verification, Change(RuntimeStateChangeOperation.Delete, Incident("incident-a", "workflow-a", "committed")), scope, "workflow-a");
            await verification.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var rollbackCheck = createContext(fixture.ConnectionString))
            Assert.Equal("committed", (await Store(rollbackCheck, scope).FindAsync("workflow-a", "incident-a"))!.Message);

        var otherScope = $"{scope}-other";
        await using (var other = createContext(fixture.ConnectionString))
            Assert.True(await Store(other, otherScope).TryAddAsync(original));

        await using (var delete = createContext(fixture.ConnectionString))
        {
            await using var transaction = await delete.Database.BeginTransactionAsync();
            await StageAsync(delete, Change(RuntimeStateChangeOperation.Delete, Incident("incident-a", "workflow-a", "committed")), scope, "workflow-a");
            await delete.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var isolation = createContext(fixture.ConnectionString))
        {
            Assert.Null(await Store(isolation, scope).FindAsync("workflow-a", "incident-a"));
            Assert.Equal("before", (await Store(isolation, otherScope).FindAsync("workflow-a", "incident-a"))!.Message);
        }

        var conflictScope = $"{scope}-conflict";
        await using (var seed = createContext(fixture.ConnectionString))
            Assert.True(await Store(seed, conflictScope).TryAddAsync(original));
        await using var stale = createContext(fixture.ConnectionString);
        _ = await stale.IncidentStates.SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(conflictScope));
        await using (var winner = createContext(fixture.ConnectionString))
            await Store(winner, conflictScope).SaveAsync(Incident("incident-a", "workflow-a", "winner"));
        await using var conflictTransaction = await stale.Database.BeginTransactionAsync();
        await StageAsync(stale, Change(RuntimeStateChangeOperation.Upsert, Incident("incident-a", "workflow-a", "stale")), conflictScope, "workflow-a");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await conflictTransaction.RollbackAsync();
        await using var conflictVerification = createContext(fixture.ConnectionString);
        Assert.Equal("winner", (await Store(conflictVerification, conflictScope).FindAsync("workflow-a", "incident-a"))!.Message);
    }

    private static EfIncidentStateStore Store(BookmarkStateDbContext context, string scope) =>
        new(context, new FixedAccessor(scope));

    private static RuntimeStateChange<IncidentState> Change(RuntimeStateChangeOperation operation, IncidentState state) =>
        new(state.IncidentId, operation, state, new Dictionary<string, string>());

    private static IncidentState Incident(string id, string workflowExecutionId, string message) => new(
        id, workflowExecutionId, null, null, IncidentSeverity.Error, IncidentStatus.Open, null,
        "TestFailure", message, CapturedAt, null, new Dictionary<string, string>());

    private static async Task StageAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<IncidentState> change,
        string scope,
        string workflowExecutionId)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointIncidentParticipantStaging")!;
        var method = type.GetMethod("StageIncidentAsync", BindingFlags.Public | BindingFlags.Static)!;
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

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
