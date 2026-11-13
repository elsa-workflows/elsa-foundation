using System.Data;
using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

/// <summary>Actual InnoDB read-view control; isolated storage, not production retention/ACK qualification.</summary>
[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeHistoryRetentionMySqlTests(RuntimeBookmarksMySqlFixture fixture)
{
    private const string Scope = "tenant";
    private const string Run = "run";
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("retention", 1, true, ["ActivityStarted"]);
    private static readonly EfSchemaChain Chain = EfSchemaChain.For(new EfSchemaFamilyDescriptor("history-retention-control", "Workflows.Runtime", "1.0.0", typeof(RuntimeHistoryRetentionMySqlTests).Assembly)
    {
        ContentColumns = new[] { typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity), typeof(RuntimeHistoryBookmarkEntity),
            typeof(RuntimeHistoryBookmarkCountEntity), typeof(RuntimeHistoryActivityEntity), typeof(RuntimeHistoryOccurrenceEntity),
            typeof(RuntimeHistoryOccurrenceMembershipEntity), typeof(RuntimeHistoryOccurrenceCounterEntity) }.Select(type => new EfSchemaColumn(type, "ContentJson")).ToArray()
    });

    [SkippableFact]
    public async Task Earlier_repeatable_read_view_cannot_report_a_stale_no_op_purge_boundary()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        foreach (var key in connection.Keys.Cast<string>().Where(key => key.Equals("Database", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)).ToArray()) connection.Remove(key);
        connection["Database"] = "history_retention_" + Guid.NewGuid().ToString("N");
        HistoryContext Open() => new(new DbContextOptionsBuilder<HistoryContext>().UseMySQL(connection.ConnectionString).Options);
        try
        {
            await using (var seed = Open())
            {
                await seed.Database.EnsureCreatedAsync();
                var native = new WorkflowExecutionState(Run, new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
                    null, Now, Now, Now, null, null, null, Scope, new Dictionary<string, string>())
                { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
                var candidate = new RuntimeHistoryCandidate(new("event", Run, 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", Now,
                    "activity", checkpointId: "logical") { Authority = RuntimeHistoryAuthority.CommittedTransition }, 0);
                var item = RuntimeHistoryIdentity.Position(candidate.Event, 1, "native", Now, 0); var mapping = new EfRuntimeHistoryProjectionMapping(Chain);
                seed.AddRange(EfWorkflowExecutionStateStore.ToEntity(native, Scope, EfWorkflowExecutionStateStore.CreateId(Scope, Run), 1),
                    mapping.Row(Scope, new RuntimeHistoryHead(Run, 1, 1, "native", Now, Policy, true, false, 0)),
                    mapping.Row(Scope, item, RuntimeHistoryIdentity.Compute(candidate)));
                await seed.SaveChangesAsync();
            }
            await using var stale = Open(); await using var oldView = await stale.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            Assert.Equal(0, (await stale.Set<RuntimeHistoryHeadEntity>().AsNoTracking().SingleAsync()).PurgedThroughSequence);
            await using (var winner = Open())
            {
                await using var currentView = await winner.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                Assert.Equal(1, await Purge(winner, 1)); await currentView.CommitAsync();
            }
            // InnoDB's non-locking SELECT still sees this older committed head after the winner.
            Assert.Equal(0, (await stale.Set<RuntimeHistoryHeadEntity>().AsNoTracking().SingleAsync()).PurgedThroughSequence);
            await Assert.ThrowsAsync<NotSupportedException>(() => Purge(stale, 0)); await oldView.RollbackAsync();
            await using var fresh = Open(); await using var latest = await fresh.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            Assert.Equal(1, await Purge(fresh, 0)); await latest.CommitAsync();
        }
        finally
        {
            // Only this control's uniquely named database is removed.
            await using var cleanup = Open(); await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static Task<long> Purge(HistoryContext context, long through) =>
        EfRuntimeHistoryRetention.StagePurgeAsync(context, new Access(), Scope, Run, through, Chain, CancellationToken.None).AsTask();
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(Scope)); }
    private sealed class HistoryContext(DbContextOptions<HistoryContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model)
        {
            RuntimeHistoryEntityConfiguration.Configure(model); RuntimeHistoryOccurrenceEntityConfiguration.Configure(model);
            foreach (var entity in model.Model.GetEntityTypes().ToArray())
            foreach (var property in entity.GetProperties().Where(property => property.ClrType == typeof(string) && property.GetMaxLength() is null).ToArray())
                model.Entity(entity.ClrType).Property(property.Name).HasColumnType("longtext");
        }
    }
}
