using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(false, "replacement")] [InlineData(true, "replacement")]
    [InlineData(false, "additional")] [InlineData(true, "additional")]
    [InlineData(false, "alternate-key")] [InlineData(true, "alternate-key")]
    public void Incompatible_history_uniqueness_cannot_qualify_history_pairing(bool independent, string constraint)
    {
        using RuntimeDbContext context = constraint switch
        {
            "replacement" => new UniqueHistoryRunIndexContext(new DbContextOptionsBuilder<UniqueHistoryRunIndexContext>().UseSqlite("Data Source=:memory:").Options),
            "additional" => new AdditionalUniqueHistoryRunIndexContext(new DbContextOptionsBuilder<AdditionalUniqueHistoryRunIndexContext>().UseSqlite("Data Source=:memory:").Options),
            _ => new AlternateHistoryRunKeyContext(new DbContextOptionsBuilder<AlternateHistoryRunKeyContext>().UseSqlite("Data Source=:memory:").Options)
        };
        var access = new HistoryCapabilityAccess();
        var reader = new EfRuntimeHistoryStore(context, access, HistoryCapabilityCodec, RuntimeHistoryEfModule.Chain);
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access);
        var observer = new EfRuntimeHistoryMutationStore(context, access, reader, TimeProvider.System, RuntimeHistoryEfModule.Chain);
        Assert.False(independent ? observer.CanRecordHistory(reader) : writer.CanCommitHistory(reader));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Unique_run_membership_index_rejects_global_and_activity_membership_for_one_run()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new UniqueHistoryRunIndexContext(new DbContextOptionsBuilder<UniqueHistoryRunIndexContext>()
            .UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Set<RuntimeHistoryOccurrenceMembershipEntity>().AddRange(Membership("global", null), Membership("activity", "activity-a"));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(19, Assert.IsType<SqliteException>(failure.InnerException).SqliteErrorCode);
    }

    private static RuntimeHistoryOccurrenceMembershipEntity Membership(string owner, string? activityScope) => new()
    {
        Id = owner, ScopeKey = "tenant-a", ScopeKeyHash = new string('a', 64), WorkflowExecutionId = "workflow-a",
        WorkflowExecutionIdHash = new string('b', 64), ContentJson = "{}", SchemaVersion = "1.0.0", Revision = 1,
        ScopeOwnerId = owner, ActivityScopeId = activityScope, ActivityExecutionId = "activity-a",
        ActivityExecutionIdHash = new string('c', 64), FirstSequence = 1, Ordinal = 0
    };

    private sealed class AdditionalUniqueHistoryRunIndexContext(DbContextOptions<AdditionalUniqueHistoryRunIndexContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryOccurrenceMembershipEntity>().HasIndex(row => new { row.ScopeKey, row.WorkflowExecutionId }).IsUnique();
        }
    }

    private sealed class AlternateHistoryRunKeyContext(DbContextOptions<AlternateHistoryRunKeyContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryOccurrenceMembershipEntity>().HasAlternateKey(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash });
        }
    }

    private sealed class UniqueHistoryRunIndexContext(DbContextOptions<UniqueHistoryRunIndexContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<RuntimeHistoryOccurrenceMembershipEntity>().HasIndex(row => new { row.ScopeKeyHash, row.WorkflowExecutionIdHash }).IsUnique();
        }
    }
}
