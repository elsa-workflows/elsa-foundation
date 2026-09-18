using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
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
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeSchedulerPoisonSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_scheduler_poison_smoke() => RuntimeSchedulerPoisonProviderSmoke.RunAsync(
        fixture,
        connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options),
        RuntimeSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeSchedulerPoisonMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_scheduler_poison_smoke() => RuntimeSchedulerPoisonProviderSmoke.RunAsync(
        fixture,
        connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options),
        RuntimeMySqlDbContext.ExpectedProviderName);
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

    private static EfWorkflowSchedulerPoisonStore Store(RuntimeDbContext context, string scope) =>
        new(context, new FixedAccessor(scope));

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
}
