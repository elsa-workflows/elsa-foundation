using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeSchedulerPoisonPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_scheduler_poison_smoke() => RuntimeSchedulerPoisonProviderSmoke.RunAsync(
        fixture,
        connection => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options),
        RuntimePostgreSqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task PostgreSql_scheduler_poison_records_a_long_execution_id() => RuntimeSchedulerPoisonProviderSmoke.RunLongExecutionIdAsync(
        fixture,
        connection => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options));

    [SkippableFact]
    public Task PostgreSql_scheduler_poison_is_recorded_only_under_the_current_ownership_fence() => RuntimeSchedulerPoisonProviderSmoke.RunFencedAsync(
        fixture,
        (connection, interceptors) => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options));
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeSchedulerPoisonSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_scheduler_poison_smoke() => RuntimeSchedulerPoisonProviderSmoke.RunAsync(
        fixture,
        connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
        RuntimeSqlServerDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task SqlServer_scheduler_poison_records_a_long_execution_id() => RuntimeSchedulerPoisonProviderSmoke.RunLongExecutionIdAsync(
        fixture,
        connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options));

    [SkippableFact]
    public Task SqlServer_scheduler_poison_is_recorded_only_under_the_current_ownership_fence() => RuntimeSchedulerPoisonProviderSmoke.RunFencedAsync(
        fixture,
        (connection, interceptors) => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).AddInterceptors(interceptors).Options));
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeSchedulerPoisonMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_scheduler_poison_smoke() => RuntimeSchedulerPoisonProviderSmoke.RunAsync(
        fixture,
        connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
        RuntimeMySqlDbContext.ExpectedProviderName);

    [SkippableFact]
    public Task MySql_scheduler_poison_records_a_long_execution_id() => RuntimeSchedulerPoisonProviderSmoke.RunLongExecutionIdAsync(
        fixture,
        connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options));

    [SkippableFact]
    public Task MySql_scheduler_poison_is_recorded_only_under_the_current_ownership_fence() => RuntimeSchedulerPoisonProviderSmoke.RunFencedAsync(
        fixture,
        (connection, interceptors) => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).AddInterceptors(interceptors).Options));
}

internal static class RuntimeSchedulerPoisonProviderSmoke
{
    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, RuntimeDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-r23-{Guid.NewGuid():N}";
        var connectionString = fixture.ConnectionString;
        var first = Record(1, "workflow-native");
        var second = Record(2, "workflow-native");

        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var store = Store(context, scope);
            await store.RecordAsync(second);
            await store.RecordAsync(first);
            var found = await store.FindAsync(first.WorkflowExecutionId, first.WorkItemId);
            Assert.NotNull(found);
            Assert.Equal(first.Fault, found!.Fault);
            Assert.Equal(first.InnerFault, found.InnerFault);
            Assert.Equal(first.Metadata, found.Metadata);
            Assert.Equal(new[] { first.WorkItemId, second.WorkItemId }, (await store.ListAsync(first.WorkflowExecutionId)).Select(item => item.WorkItemId));

            await using var transaction = await context.Database.BeginTransactionAsync();
            await store.RecordAsync(Record(3, "workflow-rollback"));
            await transaction.RollbackAsync();
        }

        await using (var restarted = createContext(connectionString))
        {
            var store = Store(restarted, scope);
            Assert.Null(await store.FindAsync("workflow-rollback", "work-3"));
            var left = await restarted.WorkflowSchedulerPoisonRecords.AsNoTracking().SingleAsync(row => row.WorkItemId == EfRelationalIdentity.Encode(first.WorkItemId));
            await using var competing = createContext(connectionString);
            var right = await competing.WorkflowSchedulerPoisonRecords.AsNoTracking().SingleAsync(row => row.Id == left.Id);
            left.Revision++;
            restarted.WorkflowSchedulerPoisonRecords.Attach(left);
            restarted.Entry(left).Property(row => row.Revision).OriginalValue = 1;
            restarted.Entry(left).State = EntityState.Modified;
            await restarted.SaveChangesAsync();
            right.Revision++;
            competing.WorkflowSchedulerPoisonRecords.Attach(right);
            competing.Entry(right).Property(row => row.Revision).OriginalValue = 1;
            competing.Entry(right).State = EntityState.Modified;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => competing.SaveChangesAsync());
        }
    }

    /// <summary>
    /// A DispatchWorkflow child execution id is 83 characters, and a keyed start's is 80. The store writes the
    /// encoded identity, which is wider than the raw identity limit: this column once held 128 characters, and
    /// a provider that enforces length rejected the poison write.
    /// </summary>
    public static async Task RunLongExecutionIdAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-long-id-{Guid.NewGuid():N}";
        var record = Record(4, new string('c', 83));

        await using (var context = createContext(fixture.ConnectionString))
        {
            await context.Database.EnsureCreatedAsync();
            await Store(context, scope).RecordAsync(record);
        }

        await using var restarted = createContext(fixture.ConnectionString);
        var store = Store(restarted, scope);
        var found = await store.FindAsync(record.WorkflowExecutionId, record.WorkItemId);
        Assert.NotNull(found);
        Assert.Equal(record.WorkflowExecutionId, found!.WorkflowExecutionId);
        Assert.Equal(record.WorkItemId, (await store.ListAsync(record.WorkflowExecutionId)).Single().WorkItemId);
    }

    /// <summary>
    /// #4032: a poison record written under the run's ownership fence. A real provider lets another connection write the
    /// ownership row while this record's transaction is open, so the two races run as they would between two nodes: the
    /// owner's heartbeat, which the record retries past, and a takeover, which refuses it.
    /// </summary>
    public static async Task RunFencedAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, IInterceptor[], RuntimeDbContext> createContext)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        const string workflow = "workflow-fenced";
        var scope = $"native-r23-fenced-{Guid.NewGuid():N}";
        var access = new FixedAccessor(scope);
        var race = new OwnershipRowRace();
        await using var context = createContext(fixture.ConnectionString, [race]);
        await context.Database.EnsureCreatedAsync();
        await using var otherConnection = createContext(fixture.ConnectionString, []);
        var store = new EfWorkflowSchedulerPoisonStore(context, access, () => Ownership(context, access));
        var lease = await Node(context, access, "node-a").AcquireAsync(workflow);

        // The item ids differ from the other smoke tests', which share this database and find a row by its item id alone.
        Assert.True(store.CanRecordFenced);
        await store.RecordFencedAsync(Record(11, workflow), lease.ToFence());
        await store.RecordFencedAsync(Record(11, workflow), lease.ToFence());
        Assert.NotNull(await store.FindAsync(workflow, "work-11"));
        var scopeKey = EfRelationalIdentity.Encode(scope);
        Assert.Equal(2, (await context.WorkflowSchedulerPoisonRecords.AsNoTracking().SingleAsync(row => row.ScopeKey == scopeKey)).Revision);

        // The owner's heartbeat commits between the fence read and the save: the record runs again and is written.
        race.Arm(async () => Assert.Equal(
            RuntimeExecutionOwnershipTransitionStatus.Applied,
            (await Node(otherConnection, access, "node-a").HeartbeatAsync(lease)).Status));
        await store.RecordFencedAsync(Record(12, workflow), lease.ToFence());
        Assert.Equal(1, race.Races);
        Assert.NotNull(await store.FindAsync(workflow, "work-12"));

        // Another node takes the run between the fence read and the save: the record is refused.
        race.Arm(async () => await Node(otherConnection, access, "node-b").AcquireAsync(workflow));
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.RecordFencedAsync(Record(13, workflow), lease.ToFence()).AsTask());
        Assert.Equal(2, race.Races);
        Assert.Null(await store.FindAsync(workflow, "work-13"));

        // The run stays taken: a later record under the old fence is refused at the fence read.
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.RecordFencedAsync(Record(14, workflow), lease.ToFence()).AsTask());
        Assert.Null(await store.FindAsync(workflow, "work-14"));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0, await context.SaveChangesAsync());
        Assert.Equal(["work-11", "work-12"], (await store.ListAsync(workflow)).Select(item => item.WorkItemId).Order(StringComparer.Ordinal));
    }

    private static EfWorkflowSchedulerPoisonStore Store(RuntimeDbContext context, string scope) =>
        new(context, new FixedAccessor(scope));

    private static EfExecutionLivenessStateStore Ownership(RuntimeDbContext context, FixedAccessor access) =>
        new(context, access, new PassThroughCodec());

    private static RuntimeExecutionOwnershipService Node(RuntimeDbContext context, FixedAccessor access, string ownerId) =>
        new(Ownership(context, access), TimeProvider.System, new RuntimeExecutionOwnershipOptions { OwnerId = ownerId });

    private static RuntimeSchedulerPoisonRecord Record(int index, string workflowExecutionId) => new(
        workflowExecutionId,
        $"work-{index}",
        WorkflowExecutionCommandKind.RunSchedulerWork,
        $"handler-{index}",
        new RuntimeFaultInfo("System.InvalidOperationException", $"boom-{index}", "stack"),
        1,
        RuntimeSchedulerPoisonDisposition.Poisoned,
        DateTimeOffset.UtcNow.AddMilliseconds(index),
        DateTimeOffset.UtcNow.AddMilliseconds(index));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class PassThroughCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    // Runs another node's write to the ownership row after the armed record has read its fence and before it saves.
    private sealed class OwnershipRowRace : ISaveChangesInterceptor
    {
        private Func<Task>? change;
        public int Races { get; private set; }

        public void Arm(Func<Task> next) => change = next;

        public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (change is { } next && eventData.Context!.ChangeTracker.Entries<ExecutionLivenessStateEntity>().Any(entry => entry.State == EntityState.Modified))
            {
                change = null;
                Races++;
                await next();
            }
            return result;
        }
    }
}
