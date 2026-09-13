using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimePlacementPostgreSqlContainerFixture.CollectionName)]
public sealed class RuntimePlacementPostgreSqlSmokeTests(RuntimePlacementPostgreSqlContainerFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_live_schema_claim_concurrency_rollback_and_reopen()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/PostgreSQL is unavailable.");
        return RuntimePlacementProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new ExecutionPlacementPostgreSqlDbContext(
                new DbContextOptionsBuilder<ExecutionPlacementPostgreSqlDbContext>().UseNpgsql(connectionString).Options),
            ExecutionPlacementPostgreSqlDbContext.ExpectedProviderName);
    }
}

[Collection(RuntimePlacementSqlServerContainerFixture.CollectionName)]
public sealed class RuntimePlacementSqlServerSmokeTests(RuntimePlacementSqlServerContainerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_live_schema_claim_concurrency_rollback_and_reopen()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/SQL Server is unavailable.");
        return RuntimePlacementProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new ExecutionPlacementSqlServerDbContext(
                new DbContextOptionsBuilder<ExecutionPlacementSqlServerDbContext>().UseSqlServer(connectionString).Options),
            ExecutionPlacementSqlServerDbContext.ExpectedProviderName);
    }
}

[Collection(RuntimePlacementMySqlContainerFixture.CollectionName)]
public sealed class RuntimePlacementMySqlSmokeTests(RuntimePlacementMySqlContainerFixture fixture)
{
    [SkippableFact]
    public Task MySql_live_schema_claim_concurrency_rollback_and_reopen()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/MySQL is unavailable.");
        return RuntimePlacementProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new ExecutionPlacementMySqlDbContext(
                new DbContextOptionsBuilder<ExecutionPlacementMySqlDbContext>().UseMySQL(connectionString).Options),
            ExecutionPlacementMySqlDbContext.ExpectedProviderName);
    }
}

internal static class RuntimePlacementProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        string connectionString,
        Func<string, ExecutionPlacementDbContext> createContext,
        string expectedProviderName)
    {
        var scope = $"provider-scope-{Guid.NewGuid():N}";
        var workflowId = $"provider-workflow-{Guid.NewGuid():N}";
        var claim = new ExecutionPlacementClaim(workflowId, "provider-node-a", Now, Now.AddMinutes(5));

        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);
            var granted = await store.TryClaimAsync(claim, Now);
            Assert.Equal(ExecutionPlacementClaimOutcome.Granted, granted.Outcome);
            AssertLease(granted.Lease, await store.FindAsync(workflowId));
            AssertLease(granted.Lease, Assert.Single(await store.ListOwnedAsync(new(claim.OwnerId, Now))));
            await store.ReleaseAsync(granted.Lease);
            Assert.Null(await store.FindAsync(workflowId));
        }

        var malformedId = $"provider-malformed-{Guid.NewGuid():N}";
        await using (var malformedAContext = createContext(connectionString))
        {
            var malformedA = Store(malformedAContext, "tenant-\uD800");
            var claimed = await malformedA.TryClaimAsync(
                new(malformedId, "provider-node-malformed-a", Now, Now.AddMinutes(5)),
                Now);
            Assert.Equal(ExecutionPlacementClaimOutcome.Granted, claimed.Outcome);
        }

        await using (var malformedBContext = createContext(connectionString))
        {
            var malformedB = Store(malformedBContext, "tenant-\uD801");
            var claimed = await malformedB.TryClaimAsync(
                new(malformedId, "provider-node-malformed-b", Now, Now.AddMinutes(5)),
                Now);
            Assert.Equal(ExecutionPlacementClaimOutcome.Granted, claimed.Outcome);
        }

        await using (var malformedReopenedContext = createContext(connectionString))
            Assert.Equal("provider-node-malformed-a", (await Store(malformedReopenedContext, "tenant-\uD800").FindAsync(malformedId))!.OwnerId);

        var rollbackId = $"provider-rollback-{Guid.NewGuid():N}";
        await using (var transactionContext = createContext(connectionString))
        {
            await using var transaction = await transactionContext.Database.BeginTransactionAsync();
            var transactionStore = Store(transactionContext, scope);
            await transactionStore.TryClaimAsync(
                new(rollbackId, "provider-node-rollback", Now, Now.AddMinutes(5)),
                Now);
            await transaction.RollbackAsync();
        }

        await using (var verificationContext = createContext(connectionString))
            Assert.Null(await Store(verificationContext, scope).FindAsync(rollbackId));

        var concurrentId = $"provider-concurrent-{Guid.NewGuid():N}";
        await using var leftContext = createContext(connectionString);
        await using var rightContext = createContext(connectionString);
        var left = Store(leftContext, scope);
        var right = Store(rightContext, scope);
        var results = await Task.WhenAll(
            left.TryClaimAsync(new(concurrentId, "provider-node-left", Now, Now.AddMinutes(5)), Now).AsTask(),
            right.TryClaimAsync(new(concurrentId, "provider-node-right", Now, Now.AddMinutes(5)), Now).AsTask());
        Assert.Single(results, result => result.Outcome == ExecutionPlacementClaimOutcome.Granted);
        Assert.Single(results, result => result.Outcome == ExecutionPlacementClaimOutcome.Denied);
        var winner = results.Single(result => result.Outcome == ExecutionPlacementClaimOutcome.Granted).Lease;
        AssertLease(winner, await Store(leftContext, scope).FindAsync(concurrentId));

        await using var reopenedContext = createContext(connectionString);
        var reopenedStore = Store(reopenedContext, scope);
        AssertLease(winner, await reopenedStore.FindAsync(concurrentId));
        await reopenedStore.ReleaseAsync(winner);
        Assert.Null(await reopenedStore.FindAsync(concurrentId));
    }

    private static EfExecutionPlacementStore Store(ExecutionPlacementDbContext context, string scope) =>
        new(context, new FixedAccessor(scope));

    private static void AssertLease(ExecutionPlacementLease expected, ExecutionPlacementLease? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.WorkflowExecutionId, actual.WorkflowExecutionId);
        Assert.Equal(expected.OwnerId, actual.OwnerId);
        Assert.Equal(expected.PlacementToken, actual.PlacementToken);
        Assert.Equal(expected.AcquiredAt, actual.AcquiredAt);
        Assert.Equal(expected.ExpiresAt, actual.ExpiresAt);
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
