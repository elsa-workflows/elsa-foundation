using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeCommandTransportPostgreSqlContainerFixture.CollectionName)]
public sealed class RuntimeCommandTransportPostgreSqlSmokeTests(RuntimeCommandTransportPostgreSqlContainerFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_live_schema_send_query_lease_ack_rollback_concurrency_and_reopen()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/PostgreSQL is unavailable.");
        return RuntimeCommandTransportProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new ExecutionCommandTransportPostgreSqlDbContext(
                new DbContextOptionsBuilder<ExecutionCommandTransportPostgreSqlDbContext>().UseNpgsql(connectionString).Options),
            ExecutionCommandTransportPostgreSqlDbContext.ExpectedProviderName);
    }
}

[Collection(RuntimeCommandTransportSqlServerContainerFixture.CollectionName)]
public sealed class RuntimeCommandTransportSqlServerSmokeTests(RuntimeCommandTransportSqlServerContainerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_live_schema_send_query_lease_ack_rollback_concurrency_and_reopen()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/SQL Server is unavailable.");
        return RuntimeCommandTransportProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new ExecutionCommandTransportSqlServerDbContext(
                new DbContextOptionsBuilder<ExecutionCommandTransportSqlServerDbContext>().UseSqlServer(connectionString).Options),
            ExecutionCommandTransportSqlServerDbContext.ExpectedProviderName);
    }
}

[Collection(RuntimeCommandTransportMySqlContainerFixture.CollectionName)]
public sealed class RuntimeCommandTransportMySqlSmokeTests(RuntimeCommandTransportMySqlContainerFixture fixture)
{
    [SkippableFact]
    public Task MySql_live_schema_send_query_lease_ack_rollback_concurrency_and_reopen()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker/MySQL is unavailable.");
        return RuntimeCommandTransportProviderSmoke.RunAsync(
            fixture.ConnectionString,
            connectionString => new ExecutionCommandTransportMySqlDbContext(
                new DbContextOptionsBuilder<ExecutionCommandTransportMySqlDbContext>().UseMySQL(connectionString).Options),
            ExecutionCommandTransportMySqlDbContext.ExpectedProviderName);
    }
}

internal static class RuntimeCommandTransportProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    public static async Task RunAsync(
        string connectionString,
        Func<string, ExecutionCommandTransportDbContext> createContext,
        string expectedProviderName)
    {
        var scope = $"provider-scope-{Guid.NewGuid():N}";
        var executionId = $"provider-execution-{Guid.NewGuid():N}";
        var firstId = $"{executionId}-first";
        var secondId = $"{executionId}-second";
        var rollbackExecutionId = $"{executionId}-rollback";

        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProviderName, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var transport = Store(context, scope);

            var first = await transport.SendAsync(executionId, Envelope(executionId, firstId, scope), Now);
            var second = await transport.SendAsync(executionId, Envelope(executionId, secondId, scope), Now.AddSeconds(1));
            Assert.Equal([1L, 2L], new[] { first.Sequence, second.Sequence });
            Assert.Equal(2, await transport.CountPendingAsync(executionId));
            Assert.Equal([executionId], await transport.ListPendingExecutionIdsAsync(Now, 10));

            var lease = Assert.Single(await transport.LeaseAsync(executionId, "provider-node-a", Now, LeaseDuration, 1));
            Assert.Equal(first.TransportItemId, lease.TransportItemId);
            Assert.Equal(2, await transport.CountPendingAsync(executionId));
            Assert.False(await transport.AckAsync(executionId, lease.TransportItemId, "provider-node-b", lease.LeaseToken!.Value, Now.AddSeconds(1)));
            Assert.True(await transport.AckAsync(executionId, lease.TransportItemId, "provider-node-a", lease.LeaseToken.Value, Now.AddSeconds(1)));

            await using var rollback = await context.Database.BeginTransactionAsync();
            context.CommandStreamHeads.Add(new ExecutionCommandStreamHeadEntity
            {
                Id = $"rollback-{Guid.NewGuid():N}",
                ScopeKey = scope,
                ScopeKeyHash = Guid.NewGuid().ToString("N"),
                WorkflowExecutionId = rollbackExecutionId,
                WorkflowExecutionIdHash = Guid.NewGuid().ToString("N"),
                WorkflowExecutionIdOrderKey = [1],
                LastSequence = 1,
                PendingCount = 0,
                PendingVisibleAtUtcTicks = 0,
                PendingSequence = 0,
                Revision = 1
            });
            await context.SaveChangesAsync();
            await rollback.RollbackAsync();
        }

        await using (var reopened = createContext(connectionString))
        {
            Assert.Null(await reopened.CommandStreamHeads.SingleOrDefaultAsync(row => row.WorkflowExecutionId == rollbackExecutionId));
            var transport = Store(reopened, scope);
            var before = await transport.CountPendingAsync(executionId);
            Assert.Equal(1, before);
            var remaining = Assert.Single(await transport.LeaseAsync(executionId, "provider-node-a", Now.AddSeconds(2), LeaseDuration, 1));
            Assert.Equal($"provider-command-{secondId}", remaining.Envelope.Command.CommandId);
            Assert.True(await transport.AckAsync(executionId, remaining.TransportItemId, "provider-node-a", remaining.LeaseToken!.Value, Now.AddSeconds(3)));
        }

        var concurrentExecutionId = $"provider-concurrent-{Guid.NewGuid():N}";
        await using var leftContext = createContext(connectionString);
        await using var rightContext = createContext(connectionString);
        var left = Store(leftContext, scope);
        var right = Store(rightContext, scope);
        var results = await Task.WhenAll(
            left.SendAsync(concurrentExecutionId, Envelope(concurrentExecutionId, "left", scope), Now).AsTask(),
            right.SendAsync(concurrentExecutionId, Envelope(concurrentExecutionId, "right", scope), Now).AsTask());
        Assert.Equal([1L, 2L], results.Select(result => result.Sequence).Order());

        var competingLeases = await Task.WhenAll(
            left.LeaseAsync(concurrentExecutionId, "provider-node-left", Now, LeaseDuration, 1).AsTask(),
            right.LeaseAsync(concurrentExecutionId, "provider-node-right", Now, LeaseDuration, 1).AsTask());
        Assert.Equal(
            [1L, 2L],
            competingLeases.SelectMany(items => items).Select(item => item.Sequence).Order());
        Assert.Equal(
            2,
            competingLeases.SelectMany(items => items).Select(item => item.TransportItemId).Distinct(StringComparer.Ordinal).Count());

        await using var finalContext = createContext(connectionString);
        var finalTransport = Store(finalContext, scope);
        Assert.Equal(2, await finalTransport.CountPendingAsync(concurrentExecutionId));
        var reopenedLease = await finalTransport.LeaseAsync(concurrentExecutionId, "provider-node-reopen", Now.Add(LeaseDuration), LeaseDuration, 2);
        Assert.Equal([1L, 2L], reopenedLease.Select(item => item.Sequence));
    }

    private static EfExecutionCommandTransport Store(ExecutionCommandTransportDbContext context, string scope) =>
        new(context, new FixedAccessor(scope));

    private static WorkflowExecutionCommandEnvelope Envelope(string executionId, string suffix, string partition) =>
        new(
            $"provider-envelope-{suffix}",
            executionId,
            new WorkflowExecutionCommand(
                $"provider-command-{suffix}",
                executionId,
                WorkflowExecutionCommandKind.RunSchedulerWork,
                Now,
                null,
                new Dictionary<string, string> { ["provider"] = suffix }),
            $"provider-idempotency-{suffix}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["provider"] = suffix },
            partition: new WorkflowExecutionPartition(partition));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
